# Rogue Stars

Raise a tiny worm into a mech-enhanced god-worm who leads an alien civilization. Dice-board roguelike
runs, auto-battles, a city of blueprint buildings staffed by breedable aliens, invasions and planetary events.

- Design spec: [docs/design/GDD.md](docs/design/GDD.md)
- Engineering conventions: [docs/ENGINEERING.md](docs/ENGINEERING.md)

```
shared/   game rules (C#, shared by client and server) + tests
server/   authoritative ASP.NET Core server + tests
client/   Unity 6.6 Android client (placeholder art)
infra/    Terraform (GCP)
```
