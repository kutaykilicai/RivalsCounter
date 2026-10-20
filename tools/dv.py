import json,sys
def decode(d):
    memo={}
    def r(i):
        if isinstance(i,int) and i<0: return None
        if i in memo: return memo[i]
        v=d[i]
        if isinstance(v,list):
            if v and isinstance(v[0],str) and v[0] in('ShallowReactive','Reactive','Ref','ShallowRef','EmptyRef'): o=r(v[1]) if len(v)>1 else None
            elif v and v[0] in('Set',): o=[r(x) for x in v[1:]]
            else: o=[r(x) for x in v]
        elif isinstance(v,dict): o={k:r(x) for k,x in v.items()}
        else: o=v
        memo[i]=o; return o
    return r(0)
