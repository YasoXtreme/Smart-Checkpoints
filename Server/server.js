const express = require("express");
const {
  createDatabase,
  initializeDatabase,
  statements,
} = require("./database.js");
const {
  createAPIKey,
  authenticateAPIKey,
  APIKeyToProjectId,
} = require("./api-key-manager.js");
const os = require("os");
require("dotenv").config({ quiet: true });
const path = require("path");

const port = process.env.PORT || 3000;
const app = express();
const db = createDatabase(path.join(__dirname, "database.db"));

app.use(express.json());
app.use(express.static(path.join(__dirname, "public")));
initializeDatabase(db);

app.post("/create-project", async (req, res) => {
  console.log(req.body);
  const projectName = req.body["project-name"];
  const APIKey = createAPIKey();
  const projectId = await statements.createProject(projectName, APIKey);
  res.send({ project_id: projectId, api_key: APIKey });
});

app.post("/create-node", authenticateAPIKey(db), async (req, res) => {
  const projectId = req.projectId;
  const xCoord = req.body["x-coord"];
  const yCoord = req.body["y-coord"];

  console.log([xCoord, yCoord]);
  const nodeId = await statements.createNode(projectId, xCoord, yCoord);

  console.log(`New node: ${nodeId}`);
  res.send({ node_id: nodeId });
});

app.post("/create-connection", authenticateAPIKey(db), async (req, res) => {
  const projectId = req.projectId;
  const fromNodeId = req.body["from-node-id"];
  const toNodeId = req.body["to-node-id"];
  const distance = req.body["distance"];
  const speedLimit = req.body["speed-limit"];

  const connectionId = await statements.createConnection(
    projectId,
    fromNodeId,
    toNodeId,
    distance,
    speedLimit,
  );
  res.send({ connection_id: connectionId });
});

app.post("/report-passing", authenticateAPIKey(db), async (req, res) => {
  const projectId = req.projectId;
  const carPlate = req.body["car-plate"];
  const nodeId = req.body["node-id"];
  const timestamp = req.body["timestamp"];

  const sightingTime = timestamp ? new Date(timestamp) : new Date();
  const violationData = await calculateViolation(
    carPlate,
    nodeId,
    sightingTime,
  );

  if (violationData.status == true) {
    console.log(
      `Car ${carPlate} is violating the speed limit!
      Going ${violationData.carSpeed} in a ${violationData.legalLimit} zone!`,
    );
  }
  await statements.sightCar(projectId, carPlate, sightingTime, nodeId, db);

  res.send(violationData);
});

app.get("/list-projects", async (req, res) => {
  const projects = await statements.listProjects(db);
  res.send(projects);
});

app.listen(port);
console.log(
  `🎧 Listening on localhost:${port}\n📌 Local Network Path: ${getWifiAddress()}:3000`,
);

function getWifiAddress() {
  const interfaces = os.networkInterfaces();
  const adapterName = "Wi-Fi 2";

  if (interfaces[adapterName]) {
    for (const info of interfaces[adapterName]) {
      if (info.family === "IPv4" && !info.internal) {
        return info.address;
      }
    }
  }
  return "Adapter not found or no IPv4 assigned";
}

function calculateTimeDifferenceInSeconds(timestamp1, timestamp2) {
  const timeDifference = timestamp2 - timestamp1;
  const timeDifferenceInSeconds = timeDifference / 1000;
  return timeDifferenceInSeconds;
}

async function calculateViolation(carPlate, nodeId, sightingTime) {
  const carData = await statements.fetchCarData(carPlate, db);
  if (!carData)
    return {
      status: false,
      carSpeed: 0,
      legalLimit: 0,
      timestamp: sightingTime,
      nodeId,
      carPlate,
    };

  const connection = await statements.getConnectionByNodes(
    carData.last_sighting_node_id,
    nodeId,
    db,
  );

  if (!connection)
    return {
      status: false,
      carSpeed: 10,
      legalLimit: 0,
      timestamp: sightingTime,
      nodeId,
      carPlate,
    };

  const carTransversalTime = calculateTimeDifferenceInSeconds(
    carData.last_sighting_time,
    sightingTime,
  );

  const maximumTransversalTime =
    (connection.distance / connection.speed_limit) * (18 / 5);
  const carSpeed = (connection.distance / carTransversalTime) * (18 / 5);
  const status = carTransversalTime < maximumTransversalTime;

  if (status) {
    console.log(carData.project_id, carPlate, carSpeed, sightingTime);
    await statements.createViolation(
      carData.project_id,
      carPlate,
      carSpeed,
      sightingTime,
      db,
    );
  }
  console.log(carSpeed, connection.speed_limit);
  console.log(carTransversalTime, maximumTransversalTime, connection.distance);

  violationData = {
    status,
    carSpeed: carSpeed,
    legalLimit: connection.speed_limit,
    timestamp: sightingTime,
    nodeId,
    carPlate,
  };

  return violationData;
}
