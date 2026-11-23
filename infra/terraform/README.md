# Rogue Stars — GCP infrastructure

Terraform for the authoritative server: Cloud Run v2 (scale to zero), Artifact Registry, Firestore (native),
Secret Manager secrets (no values), Cloud Scheduler jobs with OIDC, and least-privilege service accounts.

| Resource | Notes |
|---|---|
| `google_project_service.apis` | run, artifactregistry, firestore, secretmanager, cloudscheduler, iam, logging, androidpublisher, cloudbuild |
| `google_artifact_registry_repository.server` | Docker repo `<region>-docker.pkg.dev/<project>/rogue-stars` |
| `google_service_account.server` | Cloud Run runtime: `roles/datastore.user`, `roles/logging.logWriter`, secret accessor **per secret** |
| `google_service_account.scheduler` | Identity of the scheduler OIDC tokens (no project roles needed) |
| `google_service_account.build` | Cloud Build identity for `cloudbuild.yaml`: `artifactregistry.writer` on the repository, `logging.logWriter`, `storage.objectViewer` (source upload) |
| `google_firestore_database.main` | `(default)`, native mode, `deletion_policy = ABANDON` |
| `google_secret_manager_secret.secrets` | Play publisher credentials JSON, AppLovin SSV event key — values added manually |
| `google_cloud_run_v2_service.server` | min 0 instances, port 8080, `/health` probes (Cloud Run reserves paths ending in `z` for external callers), env config (see `docs/server.md`): Firebase auth (project `firebase_project_id`, default `project_id` = `rogue-stars-515610`), Firestore, OIDC jobs, Google Play validation (`play_package_name` = `com.oddbyte.roguestars`, `Iap__RequireAccountId=true`, `iap_mode` = `disabled` while payments are on hold, `iap_allow_test_purchases`, `iap_product_set` = `initial`), ads (`ads_mode` = `disabled` for now; `applovin` + `ads_verification`, `ads_allowed_ad_unit_ids` later) |
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

First deployment order (the Cloud Run service needs an image; the full owner runbook is `docs/setup.md`):
1. `terraform apply -var-file=dev.tfvars -target=google_project_service.apis -target=google_artifact_registry_repository.server -target=google_service_account.build -target=google_artifact_registry_repository_iam_member.build_push -target=google_project_iam_member.build_logging -target=google_project_iam_member.build_source`
2. Build and push the image on Cloud Build (no local Docker), from the repo root:
   ```bash
   gcloud builds submit --config cloudbuild.yaml \
     --service-account "$(terraform -chdir=infra/terraform output -raw build_service_account)" \
     --substitutions _REGION=europe-west1,_TAG=$(git rev-parse --short HEAD) .
   ```
3. `terraform apply -var-file=dev.tfvars -var image_tag=<tag>` (with `secrets_populated = false`).

## Secrets
Terraform creates the secrets without values. With `ads_mode = "disabled"` and the Play Developer API on the runtime
identity, nothing has to be added. Otherwise add versions out of band, then set `secrets_populated = true`
(and `play_credentials_in_secret = true` if the Play key lives in Secret Manager) and apply again so Cloud Run mounts
them as environment variables:
```bash
gcloud secrets versions add rogue-stars-play-publisher-credentials --data-file=play-service-account.json
printf '%s' "<applovin-s2s-event-key>" | gcloud secrets versions add rogue-stars-applovin-ssv-key --data-file=-
```
Without `play_credentials_in_secret` the server validates Play receipts with its own runtime identity (ADC) — invite
that service account in the Play Console. The AppLovin Event Key is only mounted with `ads_mode = "applovin"`; until it is
mounted all ad callbacks are rejected.

## Validate without cloud access
```bash
terraform fmt -check
terraform init -backend=false
terraform validate
```
`scripts/preflight.sh` runs these together with the config/secret presence checks and the test suites.
