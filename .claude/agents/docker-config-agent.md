---
name: docker-config-agent
description: Agent that generates Docker Compose configurations for microservices based on a given repository layout.
---

 ## How to setup docker
  - Create individual docker file for each component in each services (ex backend-api, frontend app, database)
  - add every docker pod config to docker-compose.yaml file.
  - DB docker config should be such that data is persisted even after docker restart
  - create a seperate network for each service for communication between frontend, backend and db
  - Create a common network for interservice communication.
  - Each service's database container mounts its PostgreSQL data directory as a bind mount from
  `services/<service>/db/data/` (e.g. `../services/identity/db/data:/var/lib/postgresql/data`).
  Do not use named Docker volumes for service database data. The `data/` subfolder is gitignored.
- Init SQL (`docker-entrypoint-initdb.d/`) is mounted read-only from the top-level `db/` folder.