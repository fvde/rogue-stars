locals {
  firebase_project = coalesce(var.firebase_project_id, var.project_id)
  image            = "${var.region}-docker.pkg.dev/${var.project_id}/${var.repository_id}/server:${var.image_tag}"

  apis = [
    "run.googleapis.com",
    "artifactregistry.googleapis.com",
    "firestore.googleapis.com",
    "secretmanager.googleapis.com",
    "cloudscheduler.googleapis.com",
    "iam.googleapis.com",
    "logging.googleapis.com",
    "androidpublisher.googleapis.com",
    "cloudbuild.googleapis.com",
  ]

  secrets = {
    play_publisher = "rogue-stars-play-publisher-credentials"
    ads_ssv_key    = "rogue-stars-applovin-ssv-key"
  }

  jobs = {
    daily-reset = {
      schedule    = "5 0 * * *"
      description = "Daily maintenance: deliver queued server mail."
    }
    weekly-arena = {
      schedule    = "15 0 * * 1"
      description = "Settle last ISO week of arena: rank rewards via mail, promotion/demotion."
    }
    invasion-start = {
      schedule    = "*/5 * * * *"
      description = "Materialise the rules calendar's active invasion occurrences as server instances."
    }
    invasion-resolve = {
      schedule    = "*/15 * * * *"
      description = "Resolve ended invasions and apply group outcomes."
    }
  }
}

# ---- APIs -----------------------------------------------------------------------------------------

resource "google_project_service" "apis" {
  for_each           = toset(local.apis)
  service            = each.value
  disable_on_destroy = false
}

# ---- Artifact Registry ----------------------------------------------------------------------------

resource "google_artifact_registry_repository" "server" {
  location      = var.region
  repository_id = var.repository_id
  format        = "DOCKER"
  description   = "Rogue Stars server images"

  depends_on = [google_project_service.apis]
}

# ---- Service accounts and least-privilege IAM -----------------------------------------------------

resource "google_service_account" "server" {
  account_id   = "rogue-stars-server"
  display_name = "Rogue Stars server (Cloud Run runtime)"
}

resource "google_service_account" "scheduler" {
  account_id   = "rogue-stars-scheduler"
  display_name = "Rogue Stars Cloud Scheduler job invoker"
}

# Cloud Build (cloudbuild.yaml) runs as this account: push images to the repository, write build logs, read the
# uploaded source tarball (gs://<project>_cloudbuild).
resource "google_service_account" "build" {
  account_id   = "rogue-stars-build"
  display_name = "Rogue Stars Cloud Build (server image)"
}

resource "google_artifact_registry_repository_iam_member" "build_push" {
  location   = google_artifact_registry_repository.server.location
  repository = google_artifact_registry_repository.server.name
  role       = "roles/artifactregistry.writer"
  member     = "serviceAccount:${google_service_account.build.email}"
}

resource "google_project_iam_member" "build_logging" {
  project = var.project_id
  role    = "roles/logging.logWriter"
  member  = "serviceAccount:${google_service_account.build.email}"
}

resource "google_project_iam_member" "build_source" {
  project = var.project_id
  role    = "roles/storage.objectViewer"
  member  = "serviceAccount:${google_service_account.build.email}"
}

resource "google_project_iam_member" "server_firestore" {
  project = var.project_id
  role    = "roles/datastore.user"
  member  = "serviceAccount:${google_service_account.server.email}"
}

resource "google_project_iam_member" "server_logging" {
  project = var.project_id
  role    = "roles/logging.logWriter"
  member  = "serviceAccount:${google_service_account.server.email}"
}

# Secret access is granted per secret, not project-wide.
resource "google_secret_manager_secret_iam_member" "server_secrets" {
  for_each  = google_secret_manager_secret.secrets
  secret_id = each.value.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.server.email}"
}

# ---- Firestore ------------------------------------------------------------------------------------

resource "google_firestore_database" "main" {
  name            = "(default)"
  location_id     = var.firestore_location
  type            = "FIRESTORE_NATIVE"
  deletion_policy = "ABANDON"

  depends_on = [google_project_service.apis]
}

# ---- Secret Manager (values are added out of band, never in Terraform) ----------------------------

resource "google_secret_manager_secret" "secrets" {
  for_each  = local.secrets
  secret_id = each.value

  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}

# ---- Cloud Run ------------------------------------------------------------------------------------

resource "google_cloud_run_v2_service" "server" {
  name                = var.service_name
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = false

  template {
    service_account = google_service_account.server.email

    scaling {
      min_instance_count = 0
      max_instance_count = var.max_instances
    }

    containers {
      image = local.image

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
        cpu_idle = true
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "RogueStars__Auth__Mode"
        value = "firebase"
      }
      env {
        name  = "RogueStars__Auth__FirebaseProjectId"
        value = local.firebase_project
      }
      env {
        name  = "RogueStars__Store__Mode"
        value = "firestore"
      }
      env {
        name  = "RogueStars__Store__FirestoreProjectId"
        value = var.project_id
      }
      env {
        name  = "RogueStars__Jobs__Mode"
        value = "oidc"
      }
      env {
        name  = "RogueStars__Jobs__Audience"
        value = var.jobs_audience
      }
      env {
        name  = "RogueStars__Jobs__ServiceAccountEmail"
        value = google_service_account.scheduler.email
      }
      env {
        name  = "RogueStars__Iap__Mode"
        value = "google"
      }
      env {
        name  = "RogueStars__Iap__PackageName"
        value = var.play_package_name
      }
      env {
        name  = "RogueStars__Iap__AllowTestPurchases"
        value = tostring(var.iap_allow_test_purchases)
      }
      env {
        name  = "RogueStars__Iap__RequireAccountId"
        value = "true"
      }
      env {
        name  = "RogueStars__Ads__Mode"
        value = "applovin"
      }
      env {
        name  = "RogueStars__Ads__Verification"
        value = var.ads_verification
      }

      dynamic "env" {
        for_each = { for i, id in var.ads_allowed_ad_unit_ids : "RogueStars__Ads__AllowedAdUnitIds__${i}" => id }
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = merge(
          var.secrets_populated ? { "RogueStars__Ads__EventKey" = "ads_ssv_key" } : {},
          var.secrets_populated && var.play_credentials_in_secret ? { "RogueStars__Iap__ServiceAccountJson" = "play_publisher" } : {},
        )
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = google_secret_manager_secret.secrets[env.value].secret_id
              version = "latest"
            }
          }
        }
      }

      startup_probe {
        http_get {
          path = "/healthz"
        }
      }

      liveness_probe {
        http_get {
          path = "/healthz"
        }
      }
    }
  }

  depends_on = [
    google_project_service.apis,
    google_project_iam_member.server_firestore,
    google_secret_manager_secret_iam_member.server_secrets,
  ]
}

# Players authenticate with Firebase ID tokens checked by the app, so the service is publicly invokable.
resource "google_cloud_run_v2_service_iam_member" "public" {
  name     = google_cloud_run_v2_service.server.name
  location = google_cloud_run_v2_service.server.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# ---- Cloud Scheduler ------------------------------------------------------------------------------

resource "google_cloud_scheduler_job" "jobs" {
  for_each         = local.jobs
  name             = "rogue-stars-${each.key}"
  description      = each.value.description
  region           = var.region
  schedule         = each.value.schedule
  time_zone        = var.scheduler_time_zone
  attempt_deadline = "600s"

  retry_config {
    retry_count = 3
  }

  http_target {
    http_method = "POST"
    uri         = "${google_cloud_run_v2_service.server.uri}/internal/jobs/${each.key}"

    oidc_token {
      service_account_email = google_service_account.scheduler.email
      audience              = var.jobs_audience
    }
  }

  depends_on = [google_project_service.apis]
}
