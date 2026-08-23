#!/usr/bin/env bash
set -euo pipefail

repo_url="${1:-https://github.com/gouldopfl/EMF_Project.git}"
backup_dir="${2:-$HOME/emf-backups/EMF_Project.git}"

mkdir -p "$(dirname "$backup_dir")"

if [[ -d "$backup_dir" ]]; then
    git -C "$backup_dir" remote update --prune
else
    git clone --mirror "$repo_url" "$backup_dir"
fi

git -C "$backup_dir" fsck --full
git -C "$backup_dir" show-ref >/dev/null

echo "Backup verified: $backup_dir"
