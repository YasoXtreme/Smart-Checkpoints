const projectsWrapper = document.getElementById("projects-wrapper");

function fillWrapper(projects) {
  projectsWrapper.innerHTML = "";
  console.log(projects);

  for (const project of projects) {
    const projectElement = document.createElement("div");
    projectElement.classList.add("project-card");
    projectElement.innerText = project;
    projectsWrapper.appendChild(projectElement);
  }
}

async function fetchProjects() {
  try {
    const response = await fetch("/list-projects");
    const projects = await response.json();
    return projects;
  } catch (error) {
    console.error("Error fetching projects:", error);
  }
}

const projects = await fetchProjects();
fillWrapper(projects);
console.log(projects);
