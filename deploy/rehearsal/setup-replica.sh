# Invoked only through authenticated Azure RunCommand on the dedicated replica.
set -euo pipefail
umask 077
python3 - <<'PY'
import json,urllib.request
request=urllib.request.Request('http://169.254.169.254/metadata/instance/compute?api-version=2021-02-01',headers={'Metadata':'true'})
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
with opener.open(request,timeout=5) as response:data=json.load(response)
assert data['resourceGroupName'].lower()=='__REHEARSAL_GROUP__'
assert data['name']=='__REHEARSAL_VM__'
assert data['subscriptionId']=='d2f70759-7eeb-4ca5-9046-f7c8d562ef8f'
PY
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install --yes --no-install-recommends docker.io docker-compose-v2 postgresql postgresql-client python3-venv curl ca-certificates openssl libnss3-tools iproute2 jq sudo openssh-server xz-utils
# This is a newly created disposable VM, never the shared Linode Docker daemon.
python3 - <<'PY'
import json,pathlib
path=pathlib.Path('/etc/docker/daemon.json')
data=json.loads(path.read_bytes()) if path.exists() else {}
data.setdefault('features',{})['containerd-snapshotter']=True
path.write_text(json.dumps(data)+'\n')
PY
systemctl restart docker
systemctl enable postgresql
systemctl start postgresql
mkdir -p /opt/pa-rehearsal /var/lib/pa-rehearsal/downloads
cd /var/lib/pa-rehearsal/downloads
curl --fail --silent --show-error --location https://nodejs.org/dist/v22.23.5/node-v22.23.5-linux-x64.tar.xz --output node.tar.xz
curl --fail --silent --show-error --location https://nodejs.org/dist/v22.23.5/SHASUMS256.txt --output SHASUMS256.txt
python3 - <<'PY'
import hashlib,pathlib
matches=[line.split()[0] for line in pathlib.Path('SHASUMS256.txt').read_text().splitlines() if line.split()[-1]=='node-v22.23.5-linux-x64.tar.xz']
assert len(matches)==1 and hashlib.sha256(pathlib.Path('node.tar.xz').read_bytes()).hexdigest()==matches[0]
PY
tar -xJf node.tar.xz -C /opt/pa-rehearsal
ln -sf /opt/pa-rehearsal/node-v22.23.5-linux-x64/bin/node /usr/local/bin/node
ln -sf /opt/pa-rehearsal/node-v22.23.5-linux-x64/bin/npm /usr/local/bin/npm
ln -sf /opt/pa-rehearsal/node-v22.23.5-linux-x64/bin/npx /usr/local/bin/npx
node --version
docker info --format '{{json .DriverStatus}}'
docker compose version
systemd-analyze --version
python3 --version
# Prevent real production API SSH and all production-shaped HTTP/registry names
# from leaving this disposable machine. No global/workstation DNS is changed.
ip address show dev lo | grep -q '170.187.142.115/32' || ip address add 170.187.142.115/32 dev lo
python3 - <<'PY'
from pathlib import Path
names=('api.playpokerattack.com','playpokerattack.com','www.playpokerattack.com','salmon-hill-0056eb60f.2.azurestaticapps.net','chefknife.azurecr.io')
path=Path('/etc/hosts');raw=path.read_text()
marker='# Owned H02 production-shaped endpoints; all are disposable loopback fixtures.\n'
if marker not in raw:path.write_text(raw+'\n'+marker+'127.0.0.1 '+' '.join(names)+'\n')
Path('/etc/ssh/sshd_config.d/00-pa-rehearsal.conf').write_text('ListenAddress 127.0.0.1\nListenAddress 170.187.142.115\nPasswordAuthentication no\nPermitRootLogin no\n')
PY
mkdir -p /run/sshd
sshd -t
systemctl restart ssh
