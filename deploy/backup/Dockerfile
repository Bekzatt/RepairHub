# Backup service. Same image as the database (deploy/docker-compose.yml, service "postgres"), so pg_dump and
# pg_restore always match the server version; keep the two tags in sync.
FROM postgres:17-alpine

COPY rh-backup.sh /usr/local/bin/rh-backup
# Strip CR in case the repository was checked out with Windows line endings.
RUN sed -i 's/\r$//' /usr/local/bin/rh-backup && chmod 755 /usr/local/bin/rh-backup

ENTRYPOINT ["rh-backup"]
CMD ["schedule"]
