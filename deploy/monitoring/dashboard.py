"""Generate the single dashboard from the collector's explicit metric contract."""
import json
from pathlib import Path

DATASOURCE={'type':'prometheus','uid':'${DS_PROMETHEUS}'}
JOIN='on(host,container_id)'
LABELS=('container','compose_project','compose_service','configured_image','image_id','registry_digest','source_revision','network_mode')


def metric(name):return 'linode_docker_'+name+'{host=~"${host:regex}"}'
SNAPSHOT=metric('snapshot_timestamp_seconds')
FRESH=f'((time() - {SNAPSHOT} < 180) and on(host) (time() - {SNAPSHOT} >= 0))'
IDENTITY='linode_docker_container_identity_timestamp_seconds{host=~"${host:regex}",container=~"${container:regex}"}'
LATEST=f'({IDENTITY} == {JOIN} group_left max by(host,container_id) ({IDENTITY}))'
PRESENT=f'(({IDENTITY} == on(host) group_left {SNAPSHOT}) or ({IDENTITY} and on(host) ({metric("inventory_success")} == 0)))'
META=f'(({LATEST} and {PRESENT}) * 0 + 1)'
ATTEMPT=f'({metric("container_sample_timestamp_seconds")} == on(host) group_left {SNAPSHOT})'
GOOD=f'(({metric("container_collection_success")} == 1) and {JOIN} {ATTEMPT} and on(host) ({metric("inventory_success")} == 1) and on(host) {FRESH})'
RUNNING=f'({GOOD} and {JOIN} ({metric("container_state")} == 2))'


def selected(expression,unknown=None,running=False):
    current=f'(({expression} and {JOIN} {RUNNING if running else GOOD}) * {JOIN} group_left({",".join(LABELS)}) {META})'
    if unknown is not None:current=f'({current} or {JOIN} ({META} * 0 + {unknown}))'
    return current


def query(name,unknown=None):return selected(metric(name),unknown)
STATE=query('container_state',0)
HEALTH=query('container_health',4)
READINESS=query('container_readiness',3)
CPU=selected('rate('+metric('container_cpu_usage_seconds_total')+'[4m])',running=True)
CPU_PERCENT=selected('(100 * rate('+metric('container_cpu_usage_seconds_total')+'[4m]) / '+JOIN+' ('+metric('container_cpu_limit_cores')+' > 0))',running=True)
MEMORY=selected(metric('container_memory_working_set_bytes'),running=True)
MEMORY_PERCENT=selected('(100 * '+metric('container_memory_usage_bytes')+' / '+JOIN+' ('+metric('container_memory_limit_bytes')+' > 0))',running=True)


def target(expr,ref='A',table=False):
    if table:expr='label_join('+expr+',"container_key","/","host","container_id")'
    return {'expr':expr,'refId':ref,'datasource':DATASOURCE,'format':'table' if table else 'time_series',
            'instant':table,'range':not table,'legendFormat':'{{host}} / {{container}} / {{container_id}}'}


def mappings(values):
    colors={'Running':'green','Healthy':'green','Ready':'green','Failed':'red','Unhealthy':'red','Not ready':'red',
            'Success':'green','Starting':'yellow','Restarting':'yellow','OOM':'red'}
    return [{'type':'value','options':{str(key):{'text':value,'color':colors.get(value,'gray')} for key,value in values.items()}}]


def build():
    panels=[]
    def panel(title,kind,x,y,w,h,targets,description='',defaults=None):
        item={'id':len(panels)+1,'title':title,'type':kind,'datasource':DATASOURCE,'description':description,
            'gridPos':{'x':x,'y':y,'w':w,'h':h},'targets':targets,
            'fieldConfig':{'defaults':{'noValue':'Unavailable','color':{'mode':'palette-classic'},**(defaults or {})},'overrides':[]},
            'options':{}}
        panels.append(item);return item
    def table(title,y,queries,fields=None,description=''):
        item=panel(title,'table',0,y,24,8,[target(expr,chr(65+i),True) for i,(_,expr,_) in enumerate(queries)],description)
        item['options']={'showHeader':True,'cellHeight':'lg','footer':{'show':False}}
        rename={};exclude={'Time':True,'container_key':True}
        for i,(name,_,config) in enumerate(queries):
            field='Value' if len(queries)==1 else 'Value #'+chr(65+i)
            rename[field]=name
            if config.get('custom.hidden'):exclude[field]=True
            item['fieldConfig']['overrides'].append({'matcher':{'id':'byName','options':name},
                'properties':[{'id':key,'value':value} for key,value in config.items() if key!='custom.hidden']})
        if fields:
            exclude.update({field:True for field in ('host','container_id',*LABELS) if field not in fields})
        if len(queries)>1:item['transformations']=[{'id':'joinByField','options':{'byField':'container_key','mode':'outerTabular'}}]
        else:item['transformations']=[]
        item['transformations'].append({'id':'organize','options':{'excludeByName':exclude,'renameByName':rename,
            'indexByName':{field:index for index,field in enumerate(['host','container','container_id',*rename])}}})
        item['fieldConfig']['defaults']['custom']={'align':'auto','cellOptions':{'type':'auto'},'wrapText':True}
        return item
    def chart(title,x,y,exprs,unit,description=''):
        item=panel(title,'timeseries',x,y,12,8,[target(expr,chr(65+i)) for i,expr in enumerate(exprs)],description,{'unit':unit,'min':0})
        item['options']={'legend':{'displayMode':'table','placement':'bottom','calcs':['lastNotNull']},'tooltip':{'mode':'multi','sort':'desc'}}
        item['fieldConfig']['defaults']['custom']={'drawStyle':'line','lineInterpolation':'linear','lineWidth':1,'fillOpacity':8,'spanNulls':False}
        return item
    collection=f'(({metric("collection_success")} and on(host) {FRESH}) or on(host) ({metric("inventory_success")} * 0 + 2))'
    item=panel('Collection result','stat',0,0,8,4,[target(collection)],'Unknown/stale after three minutes, or absent. Partial collection is Failed; successful peers remain visible.',
               {'mappings':mappings({0:'Failed',1:'Success',2:'Unknown / stale'}),'noValue':'Unknown / missing'})
    item['options']={'reduceOptions':{'calcs':['last'],'fields':'','values':False},'textMode':'value_and_name','colorMode':'value'}
    for title,x,expr in [('Snapshot age',8,'time() - '+SNAPSHOT),('Last complete success age',16,'time() - '+metric('last_success_timestamp_seconds'))]:
        item=panel(title,'stat',x,0,8,4,[target(expr)],'Age, not a deployment observation gate.',{'unit':'s','min':0})
        item['options']={'reduceOptions':{'calcs':['last'],'fields':'','values':False},'textMode':'value_and_name'}
    table('Image identity (full copyable values)',4,[('Inventory',META,{'custom.hidden':True})],description='Identity includes stopped containers. Requested image, local ID and matching registry digest are different fields; source may be Not provided. Old metadata is excluded by identity timestamp. Full values wrap and remain copyable.')
    table('Current status, health and readiness',12,[
        ('State',STATE,{'mappings':mappings({0:'Unknown / stale',1:'Created',2:'Running',3:'Paused',4:'Restarting',5:'Removing',6:'Exited',7:'Dead'})}),
        ('Docker health',HEALTH,{'mappings':mappings({0:'Not configured',1:'Healthy',2:'Unhealthy',3:'Starting',4:'Unknown / stale'})}),
        ('Application readiness',READINESS,{'mappings':mappings({0:'Not configured',1:'Ready',2:'Not ready',3:'Unknown / stale'})}),
        ('Network attribution',query('container_network_attributable'),{'mappings':mappings({0:'Shared / unavailable',1:'Container namespace'})})],
        ['host','container','container_id'],description='Running is not health or gameplay correctness. No healthcheck says Not configured. Failed/stale collection says Unknown, never retained green. Fixed loopback readiness is separate from Docker health.')
    table('Lifecycle of each container incarnation',20,[
        ('Created',selected(metric('container_created_timestamp_seconds')+' * 1000'),{'unit':'dateTimeAsIso'}),
        ('Last start',selected(metric('container_started_timestamp_seconds')+' * 1000'),{'unit':'dateTimeAsIso'}),
        ('Last finish',selected(metric('container_finished_timestamp_seconds')+' * 1000'),{'unit':'dateTimeAsIso'}),
        ('Uptime',selected('time() - '+metric('container_started_timestamp_seconds'),running=True),{'unit':'s'}),
        ('Restarts',query('container_restarts'),{'decimals':0}),
        ('OOM',query('container_oom_killed'),{'mappings':mappings({0:'No',1:'OOM'})}),
        ('Exit code',query('container_exit_code'),{'decimals':0})],['host','container','container_id'],
        'Restart/OOM/exit data describe this Docker incarnation, not lifetime deployment history. Missing timestamps or exit codes are unavailable.')
    table('Configured limits',28,[('CPU quota',query('container_cpu_limit_cores'),{'unit':'cores','mappings':mappings({0:'Unlimited'})}),
        ('Memory limit',query('container_memory_limit_bytes'),{'unit':'bytes','mappings':mappings({0:'Unlimited'})})],['host','container','container_id'],
        'Zero quota/limit explicitly means Unlimited. Resource percentages are shown only for positive configured limits; CPU affinity is not quota.')
    chart('CPU usage (cores)',0,36,[CPU],'cores','One core = one CPU second per second. Four-minute rate; separate container IDs are never merged.')
    chart('CPU usage / configured quota',12,36,[CPU_PERCENT],'percent','No configured quota means Unlimited; no percentage is fabricated.')
    chart('Memory working set',0,44,[MEMORY],'bytes','Docker memory usage minus inactive file cache. Absent cache input means unavailable.')
    chart('Memory usage / configured limit',12,44,[MEMORY_PERCENT],'percent','Raw Docker usage divided by a positive configured hard limit; Unlimited has no percentage.')
    network=chart('Network receive / transmit',0,52,[selected('rate('+metric('container_network_'+direction+'_bytes_total')+'[4m])',running=True) for direction in ('receive','transmit')],
          'Bps','Host/container-shared namespaces say Shared/unavailable in the status table. Their counters are omitted; this is not zero traffic.')
    block=chart('Block read / write',12,52,[selected('rate('+metric('container_block_'+direction+'_bytes_total')+'[4m])',running=True) for direction in ('read','write')],
          'Bps','Four-minute counter rates across block devices, distinct per container incarnation. Missing kernel I/O accounting remains unavailable.')
    for item,directions in ((network,('Receive','Transmit')),(block,('Read','Write'))):
        for target_item,direction in zip(item['targets'],directions):target_item['legendFormat']=direction+' / '+target_item['legendFormat']
    item=panel('Sampled lifecycle state','state-timeline',0,60,24,8,[target(STATE)],'Thirty-second collection / sixty-second scrape can miss brief transitions. Removed incarnations have no current state; old IDs retain history.',
               {'mappings':mappings({0:'Unknown / stale',1:'Created',2:'Running',3:'Paused',4:'Restarting',5:'Removing',6:'Exited',7:'Dead'})})
    item['options']={'showValue':'auto','mergeValues':True,'rowHeight':0.8,'legend':{'displayMode':'list','placement':'bottom'}}
    return {'uid':'linode-docker-containers','title':'Linode Docker Containers','schemaVersion':41,'version':1,
        'description':'Shared host Docker inventory, full image identity, lifecycle, health and resources. No deployment gate, synthetic program or automatic recovery.',
        'tags':['linode','docker'],'timezone':'browser','refresh':'30s','time':{'from':'now-1h','to':'now'},'editable':True,
        '__inputs':[{'name':'DS_PROMETHEUS','label':'Existing Prometheus datasource','type':'datasource','pluginId':'prometheus','pluginName':'Prometheus'}],
        'templating':{'list':[
            {'name':'host','label':'Host','type':'query','datasource':DATASOURCE,'query':'label_values(linode_docker_inventory_success, host)',
             'refresh':2,'multi':True,'includeAll':True,'allValue':'.*','current':{'text':['expense-tracker-vm'],'value':['expense-tracker-vm']},'sort':1},
            {'name':'container','label':'Container','type':'query','datasource':DATASOURCE,
             'query':'label_values(linode_docker_container_info{host=~"${host:regex}"}, container)',
             'refresh':2,'multi':True,'includeAll':True,'allValue':'.*','current':{'text':'All','value':'$__all'},'sort':1}]},
        'panels':panels,'annotations':{'list':[]}}


if __name__=='__main__':
    Path(__file__).with_name('linode-docker-dashboard.json').write_text(json.dumps(build(),indent=2)+'\n',encoding='utf-8')
