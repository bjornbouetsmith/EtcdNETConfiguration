# Provider for configuration systems in .NET

Uses ETCD as its backing store.

## Testing

Deploy ETCD in docker:
```sh
cd Source
docker-compose up -d
```
Check the status of the environment:
```sh
docker ps

CONTAINER ID   IMAGE                        COMMAND                  CREATED          STATUS          PORTS           NAMES
0c6b9a29ebac   quay.io/coreos/etcd:v3.6.5   "etcd --name=etcd --…"   37 seconds ago   Up 21 seconds   2379-2380/tcp   source-etcd-1

```