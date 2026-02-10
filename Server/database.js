const sqlite = require("sqlite3");

function createDatabase() {
  return new sqlite.Database("Server/database.db");
}

function initializeDatabase(db) {
  db.serialize(() => {
    db.run(`CREATE TABLE IF NOT EXISTS projects (
        project_id INTEGER PRIMARY KEY AUTOINCREMENT,
        project_name TEXT,
        api_key TEXT
    )`);

    db.run(`CREATE TABLE IF NOT EXISTS nodes (
        node_id INTEGER PRIMARY KEY AUTOINCREMENT,
        project_id INTEGER,
        x_coord REAL,
        y_coord REAL,
        FOREIGN KEY(project_id) REFERENCES projects(project_id)
    )`);

    db.run(`CREATE TABLE IF NOT EXISTS connections (
        connection_id INTEGER PRIMARY KEY AUTOINCREMENT,
        project_id INTEGER,
        from_node_id INTEGER, 
        to_node_id INTEGER,
        distance REAL,
        speed_limit REAL,
        FOREIGN KEY(project_id) REFERENCES projects(project_id),
        FOREIGN KEY(from_node_id) REFERENCES nodes(node_id),
        FOREIGN KEY(to_node_id) REFERENCES nodes(node_id)
    )`);

    db.run(`CREATE TABLE IF NOT EXISTS car_data (
        car_id INTEGER PRIMARY KEY AUTOINCREMENT,
        project_id INTEGER,
        car_plate TEXT,
        last_sighting_time INT,
        last_sighting_node_id INTEGER,
        FOREIGN KEY(project_id) REFERENCES projects(project_id),
        FOREIGN KEY(last_sighting_node_id) REFERENCES nodes(node_id)
    )`);

    db.run(`CREATE TABLE IF NOT EXISTS violations (
        violation_id INTEGER PRIMARY KEY AUTOINCREMENT,
        project_id INTEGER,
        car_plate TEXT,
        car_speed REAL,
        timestamp TEXT,
        FOREIGN KEY(project_id) REFERENCES projects(project_id)
    )`);
  });
}

function createPlaceholders(object) {
  return Object.keys(object)
    .map(() => "?")
    .join(", ");
}

function addEntry(tableName = "", entry = {}, db = createDatabase()) {
  return new Promise((resolve, reject) => {
    db.run(
      `INSERT INTO ${tableName} (${Object.keys(entry).join(", ")}) VALUES (${createPlaceholders(entry)})`,
      Object.values(entry),
      function (err) {
        if (err) {
          console.error("Database Error:", err.message);
          return reject(err);
        }

        console.log(`A row has been inserted with row id ${this.lastID}`);
        resolve(this.lastID);
      },
    );
  });
}

function isCarPlateRegistered(projectId, carPlate, db = createDatabase()) {
  return new Promise((resolve, reject) => {
    db.get(
      "SELECT * FROM car_data WHERE project_id = ? AND car_plate = ?",
      [projectId, carPlate],
      (err, row) => {
        if (err) {
          reject(err);
        } else if (row) {
          resolve(true);
        } else {
          resolve(false);
        }
      },
    );
  });
}

function registerCarPlate(projectId, carPlate, db = createDatabase()) {
  return new Promise((resolve, reject) => {
    db.run(
      "INSERT INTO car_data (project_id, car_plate) VALUES (?, ?)",
      [projectId, carPlate],
      function (err) {
        if (err) {
          reject(err);
        } else {
          resolve(this.lastID);
        }
      },
    );
  });
}

function carPlateToCarId(projectId, carPlate, db = createDatabase()) {
  return new Promise((resolve, reject) => {
    db.get(
      "SELECT * FROM car_data WHERE project_id = ? AND car_plate = ?",
      [projectId, carPlate],
      (err, row) => {
        if (err) {
          reject(err);
        } else if (row) {
          resolve(row.car_id);
        } else {
          reject(new Error("Car plate not found"));
        }
      },
    );
  });
}

const statements = {
  createProject: async (projectName, apiKey, db) => {
    return await addEntry(
      "projects",
      { project_name: projectName, api_key: apiKey },
      db,
    );
  },
  createNode: async (projectId, xCoord, yCoord, db) => {
    const nodeId = await addEntry(
      "nodes",
      {
        project_id: projectId,
        x_coord: xCoord,
        y_coord: yCoord,
      },
      db,
    );
    console.log(nodeId);
    return nodeId;
  },
  createConnection: async (
    projectId,
    fromNodeId,
    toNodeId,
    distance,
    speedLimit,
    db,
  ) => {
    return await addEntry(
      "connections",
      {
        project_id: projectId,
        from_node_id: fromNodeId,
        to_node_id: toNodeId,
        distance: distance,
        speed_limit: speedLimit,
      },
      db,
    );
  },
  createViolation: async (projectId, carPlate, carSpeed, timestamp, db) => {
    console.log(projectId, carPlate, carSpeed, timestamp);
    return await addEntry(
      "violations",
      {
        project_id: projectId,
        car_plate: carPlate,
        car_speed: carSpeed,
        timestamp: timestamp,
      },
      db,
    );
  },
  createCarData: async (
    projectId,
    carPlate,
    lastSightingTime,
    lastSightingNodeId,
    db,
  ) => {
    return await addEntry(
      "car_data",
      {
        project_id: projectId,
        car_plate: carPlate,
        last_sighting_time: lastSightingTime,
        last_sighting_node_id: lastSightingNodeId,
      },
      db,
    );
  },
  sightCar: async (
    projectId,
    carPlate,
    newSightingTime,
    newSightingNodeId,
    db,
  ) => {
    const isRegistered = await isCarPlateRegistered(projectId, carPlate);
    const carId = isRegistered
      ? await carPlateToCarId(projectId, carPlate, db)
      : await registerCarPlate(projectId, carPlate, db);

    db.run(
      "UPDATE car_data SET last_sighting_time = ?, last_sighting_node_id = ? WHERE car_id = ?",
      [newSightingTime, newSightingNodeId, carId],
      function (err) {
        if (err) {
          console.error(err.message);
        }
        console.log(`Car ${this.lastID} is on ${newSightingNodeId}`);
      },
    );
  },
  getConnectionByNodes: (fromNodeId, toNodeId, db) => {
    return new Promise((resolve, reject) => {
      db.get(
        "SELECT * FROM connections WHERE from_node_id = ? AND to_node_id = ?",
        [fromNodeId, toNodeId],
        (err, row) => {
          if (err) {
            reject(err);
            return;
          }
          resolve(row);
        },
      );
    });
  },
  fetchCarData: (carPlate, db) => {
    return new Promise((resolve, reject) => {
      db.get(
        "SELECT * FROM car_data WHERE car_plate = ?",
        [carPlate],
        (err, row) => {
          if (err) {
            reject(err);
            return;
          }
          resolve(row);
        },
      );
    });
  },
};

module.exports = {
  createDatabase,
  initializeDatabase,
  statements,
};
