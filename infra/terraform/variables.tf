variable "project_id" {
  description = "GCP project id (also the Firebase project unless firebase_project_id is set)."
  type        = string
}

variable "region" {
  description = "Region for Cloud Run, Artifact Registry and Cloud Scheduler."
  type        = string
  default     = "europe-west1"
}

variable "firestore_location" {
  description = "Firestore database location (region or multi-region such as eur3)."
  type        = string
  default     = "europe-west1"
}

variable "image_tag" {
  description = "Tag of the server image in Artifact Registry to deploy (e.g. the git sha)."
  type        = string
}

variable "service_name" {
  description = "Cloud Run service name."
  type        = string
  default     = "rogue-stars-server"
}

variable "repository_id" {
  description = "Artifact Registry Docker repository id."
  type        = string
  default     = "rogue-stars"
}

variable "max_instances" {
  description = "Upper bound of Cloud Run instances (min is 0: scale to zero)."
  type        = number
  default     = 10
}

variable "firebase_project_id" {
  description = "Firebase project issuing player ID tokens. Defaults to project_id."
  type        = string
  default     = null
}

variable "play_package_name" {
  description = "Android package name used for Google Play receipt validation."
  type        = string
  default     = "com.roguestars.game"
}

variable "secrets_populated" {
  description = "Set to true once the Secret Manager secrets have a version; until then they are not mounted into Cloud Run."
  type        = bool
  default     = false
}

variable "jobs_audience" {
  description = "Audience of the Cloud Scheduler OIDC tokens; the server only accepts job calls carrying it."
  type        = string
  default     = "rogue-stars-jobs"
}

variable "scheduler_time_zone" {
  description = "Time zone of the scheduled jobs (arena weeks are ISO weeks in UTC)."
  type        = string
  default     = "Etc/UTC"
}
