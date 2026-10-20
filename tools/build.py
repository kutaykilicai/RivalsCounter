import json,re,datetime,os,subprocess
from dv import decode
chars=json.load(open('chars.json'))
tl=list(decode(json.load(open('tlp.json')))['data'].values())[0]
mm=decode(json.load(open('mm.json')))['data']['hero-matchups']
gg=json.load(open('gg_tiers.json'))
role={'Tank':'Vanguard','Damage':'Duelist','Support':'Strategist'}
def slug(n): return re.sub(r'[^a-z0-9]+','-',n.lower().replace('&','')).strip('-')
gmap={}
for t,l in gg.items():
    for s in l: gmap.setdefault(s,[]).append(t)
# overall wr per hero across all ranks
agg={}
for g in tl['heroes']:
    for h in g['heroes']:
        a=agg.setdefault(h['hero_id'],[0,0,0]); a[0]+=h['wr_matches'];a[1]+=h['wr_wins'];a[2]+=h['matches']
total_matches=sum(v[2] for v in agg.values())/12.0
heroes=[]
dp={'Deadpool (Vanguard)':'S','Deadpool (Duelist)':'B','Deadpool (Strategist)':'C'}
alias={'spider-man':'spider-man','cloak-dagger':'cloak-dagger','hulk':'bruce-banner','star-lord':'star-lord'}
for c in chars:
    hid=c['hero_id'];s=slug(c['name']);s=alias.get(s,s)
    t=dp.get(c['name']) or (gmap.get(s,[None])[0])
    a=agg.get(hid,[0,0,0])
    tus=[{'id':u['id'],'name':u['name'].title(),'heroes':u['heroes'],'text':u['text']} for u in c.get('Teamup',[]) if u.get('currentlyActive')]
    heroes.append({'id':hid,'name':c['name'],'role':role[c['role']],'tier':t,'winRate':round(a[1]/a[0]*100,2) if a[0] else None,'matches':a[2],'pickRate':round(a[2]/total_matches*100,2) if a[2] else None,'icon':f'icons/{hid}.png','teamUps':tus})
base={}
for c,row in mm.items():
    M=sum(v['matches'] for k,v in row.items() if k!=c); W=sum(v['wins'] for k,v in row.items() if k!=c)
    base[int(c)]=100-W/M*100 if M else None  # row = opponents' wins vs hero
counters={}
MIN=1500
for e,row in mm.items():
    e=int(e); out=[]
    for c,v in row.items():
        c=int(c)
        if c==e or v['matches']<MIN or c not in base or base[c] is None: continue
        wr=v['wins']/v['matches']*100  # mm[E][C].wins = C's wins vs E (verified vs rivalsmeta Counters table)
        out.append({'hero':c,'wrVs':round(wr,2),'delta':round(wr-50,2),'lift':round(wr-base[c],2),'matches':v['matches'],'siteDiff':round(2*(wr-50),2),'source':'rivalsmeta.com/characters/%s/matchups'%slug([h['name'] for h in heroes if h['id']==e][0])})
    out.sort(key=lambda x:-x['delta'])
    counters[e]=out
ts=datetime.datetime.fromtimestamp(tl['timestamp'],datetime.UTC).strftime('%Y-%m-%d')
data={'generated':datetime.date.today().isoformat(),'patch':'Season 10.5 (balance patch 20261009)','tierDate':'Oct 14, 2026','rivalsmetaStatsDate':ts,'rivalsmetaSeasonId':tl['season'],
 'sources':{'roster_teamups':'https://rivalsmeta.com/characters','matchups':'https://rivalsmeta.com/characters/<hero>/matchups (head-to-head win rates, all ranks)','winrate':'https://rivalsmeta.com/tier-list','tier':'https://marvelrivals.gg/best-heroes-meta-tier-list/ (Season 10.5, updated 2026-10-14, Grand Master+)'},
 'notes':'delta = WR vs enemy - 50 (= rivalsmeta Difference column / 2); WR vs enemy = rivalsmeta Counters table (Diamond+) (delta, percentage points). Min %d head-to-head matches.'%MIN,
 'heroes':heroes,'counters':{str(k):v for k,v in counters.items()}}
json.dump(data,open('heroes.json','w'),ensure_ascii=False,separators=(',',':'))
print(ts,len(heroes),[h['name'] for h in heroes if not h['tier']], os.path.getsize('heroes.json'))
