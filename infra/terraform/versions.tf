terraform {
  required_version = ">= 1.6"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }

  # Remote state: configure a GCS bucket at init time, e.g.
  #   terraform init -backend-config="bucket=<state-bucket>" -backend-config="prefix=rogue-stars"
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}
