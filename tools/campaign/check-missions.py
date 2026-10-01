"""Static check of converted campaign maps: criteria and actions the runtime lacks, and references to
regions, special forces, units and messages that do not exist.   python tools/campaign/check-missions.py"""
import os, re, glob, collections, sys
root = os.path.join(os.environ['APPDATA'], 'OpenRA', 'maps', 'dr', 'campaign')
SUPPORTED_C = set('critand critor critnot critonce crittimer crittimergame critdestroyunit critdestroybuilding critdestroything critenemyinregion critinregion critteaminregion critholdregion critharassregion critmoveunitstoregion crithavecredits critbuildbuilding critbeginbuildbuilding critbuildunit critkillteamunits critdestroyteambuildings critkillenemyunits critdestroyenemybuildings critkillall critkillallandallies critdestroybuildingtype critkillunittype critmoreunitsthanenemy critlessunitsthanenemy critcollectwater critcollectmineral critstealplan'.split())
SUPPORTED_A = set('triggerspecialforces releasespecialforces givespecialforces adjustregionpri bonuscredits setaipfile triggermessage setalliance setmessagefile triggerevent definecondition'.split())
units = collections.Counter(); acts = collections.Counter(); problems = collections.defaultdict(list)
for d in sorted(glob.glob(os.path.join(root, 'm*'))):
    name = os.path.basename(d)
    scn = open(glob.glob(d + '/*.scn')[0], encoding='latin1').read()
    regions = set(int(x) for x in re.findall(r'DefineRegion\s*\(\s*(\d+)', scn))
    sfs = set(int(x) for x in re.findall(r'DefineSpecialForces\s*\(\s*(\d+)', scn))
    mapyaml = open(d + '/map.yaml', encoding='utf-8').read()
    ids = set(int(x) for x in re.findall(r'^\tu(\d+):', mapyaml, re.M))
    msgs = set(l.split('\t')[0].lower() for l in open(d + '/messages.txt', encoding='utf-8') if '\t' in l)
    for f in glob.glob(d + '/*.fsm') + glob.glob(d + '/*.end'):
        if os.path.basename(f).startswith('def_'): continue
        t = re.sub(r';[^\n]*', '', open(f, encoding='latin1').read())
        for m in re.finditer(r'(\w+)\s*\(([^)]*)\)\s*(\{[^{}]*\})?', t):
            cmd, args, block = m.group(1), m.group(2).split(), m.group(3)
            lc = cmd.lower()
            if lc.startswith('crit'):
                units[lc] += 1
                if lc not in SUPPORTED_C: problems[name].append('criterion ' + cmd)
            elif lc not in ('defineaicondtree', 'defineendcondtree', 'definecondstate'):
                acts[lc] += 1
                if lc not in SUPPORTED_A: problems[name].append('action ' + cmd)
            if lc in ('critenemyinregion','critinregion','critteaminregion','critholdregion','critharassregion','critmoveunitstoregion','adjustregionpri') and args and int(args[0]) not in regions:
                problems[name].append(f'{os.path.basename(f)}: {cmd} region {args[0]} undefined')
            if lc in ('critbuildbuilding','critbeginbuildbuilding') and len(args) > 1 and args[1] != '0' and int(args[1]) not in regions:
                problems[name].append(f'{os.path.basename(f)}: {cmd} region {args[1]} undefined')
            if lc == 'triggerspecialforces':
                if int(args[0]) not in sfs: problems[name].append(f'{os.path.basename(f)}: sf {args[0]} undefined')
                if int(args[1]) not in regions: problems[name].append(f'{os.path.basename(f)}: sf region {args[1]} undefined')
            if lc in ('releasespecialforces','givespecialforces') and int(args[0]) not in sfs:
                problems[name].append(f'{os.path.basename(f)}: sf {args[0]} undefined')
            if lc in ('critdestroyunit','critdestroybuilding','critmoveunitstoregion') and block:
                for i in re.findall(r'\d+', block):
                    if int(i) not in ids: problems[name].append(f'{os.path.basename(f)}: {cmd} id {i} not on map')
            if lc == 'triggermessage' and args and args[0].strip('"').lower() not in msgs:
                problems[name].append(f'message {args[0]} has no text')
print('criteria', dict(units)); print('actions', dict(acts))
for k, v in problems.items():
    c = collections.Counter(v)
    print(k, '; '.join(f'{x}' + (f' x{n}' if n > 1 else '') for x, n in c.items()))
