output "service_url" {
  description = "Public URL of the Cloud Run service (client base URL)."
  value       = google_cloud_run_v2_service.server.uri
}

output "image" {
  description = "Image reference deployed to Cloud Run."
  value       = local.image
}

output "artifact_registry" {
  description = "Docker repository to push server images to."
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.server.repository_id}"
}

output "server_service_account" {
  description = "Runtime service account of the server."
  value       = google_service_account.server.email
}

output "scheduler_service_account" {
  description = "Service account Cloud Scheduler signs job OIDC tokens as."
  value       = google_service_account.scheduler.email
}

output "secret_ids" {
  description = "Secret Manager secrets that need a version before setting secrets_populated = true."
  value       = { for k, s in google_secret_manager_secret.secrets : k => s.secret_id }
}
