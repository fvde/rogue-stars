# Rogue Stars — GCP infrastructure

Terraform for the authoritative server: Cloud Run v2 (scale to zero), Artifact Registry, Firestore (native),
Secret Manager secrets (no values), Cloud Scheduler jobs with OIDC, and least-privilege service accounts.

| Resource | Notes |
|---|---|
| `google_project_service.apis` | run, artifactregistry, firestore, secretmanager, cloudscheduler, iam, logging, androidpublisher |
| `google_artifact_registry_repository.server` | Docker repo `<region>-docker.pkg.dev/<project>/rogue-stars` |
| `google_service_account.server` | Cloud Run runtime: `roles/datastore.user`, `roles/logging.logWriter`, secret accessor **per secret** |
| `google_service_account.scheduler` | Identity of the scheduler OIDC tokens (no project roles needed) |
| `google_firestore_database.main` | `(default)`, native mode, `deletion_policy = ABANDON` |
| `google_secret_manager_secret.secrets` | Play publisher credentials JSON, AppLovin SSV event key — values added manually |
| `google_cloud_run_v2_service.server` | min 0 instances, port 8080, `/healthz` probes, env config (see `docs/server.md`) |
| `google_cloud_scheduler_job.jobs` | `daily-reset` 00:05, `weekly-arena` Mon 00:15, `invasion-start` every 5 min, `invasion-resolve` every 15 min (UTC) |

The service is publicly invokable (`allUsers` → `run.invoker`): players authenticate with Firebase ID tokens that
the app verifies; `/internal/jobs/*` only accepts Google OIDC tokens for the scheduler service account with
audience `jobs_audience`.

## Prerequisites
- Terraform ≥ 1.6, `gcloud` authenticated with a principal allowed to manage the project.
- A GCS bucket for remote state.
- Firebase enabled on the project (or set `firebase_project_id`).

## Plan / apply
```bash
cd infra/terraform
cp example.tfvars dev.tfvars            # *.tfvars (except example.tfvars) are git-ignored
terraform init -backend-config="bucket=<state-bucket>" -backend-config="prefix=rogue-stars/dev"
terraform plan  -var-file=dev.tfvars -out=plan.out
terraform apply plan.out
```

First deployment order (the Cloud Run service needs an image):
1. `terraform apply -var-file=dev.tfvars -target=google_project_service.apis -target=google_artifact_registry_repository.server`
2. Build and push the image from the repo root:
   ```bash
   docker build -f server/Dockerfile -t europe-west1-docker.pkg.dev/<project>/rogue-stars/server:<tag> .
   docker push europe-west1-docker.pkg.dev/<project>/rogue-stars/server:<tag>
   ```
3. `terraform apply -var-file=dev.tfvars -var image_tag=<tag>`

## Secrets
Terraform creates the secrets without values. Add versions out of band, then set `secrets_populated = true`
and apply again so Cloud Run mounts them as environment variables:
```bash
gcloud secrets versions add rogue-stars-play-publisher-credentials --data-file=play-service-account.json
printf '%s' "<applovin-s2s-event-key>" | gcloud secrets versions add rogue-stars-applovin-ssv-key --data-file=-
```
Until then the server validates Play receipts with its own runtime identity (ADC) — grant that service account
access in the Play Console, or populate the secret — and rejects all ad callbacks.

## Validate without cloud access
```bash
terraform fmt -check
terraform init -backend=false
terraform validate
```
