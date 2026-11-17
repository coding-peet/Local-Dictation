"""Reproducible text metrics for the local ASR comparison."""
import re
import unicodedata


def normalize(text):
    return ' '.join(re.findall(r'\w+',unicodedata.normalize('NFC',text).casefold(),flags=re.UNICODE))


def alignment(ref,hyp):
    rows=[[0]*(len(hyp)+1) for _ in range(len(ref)+1)]
    for i in range(len(ref)+1): rows[i][0]=i
    for j in range(len(hyp)+1): rows[0][j]=j
    for i,a in enumerate(ref,1):
        for j,b in enumerate(hyp,1):
            rows[i][j]=min(rows[i-1][j]+1,rows[i][j-1]+1,rows[i-1][j-1]+(a!=b))
    i,j=len(ref),len(hyp); edits=[]
    while i or j:
        if i and j and rows[i][j]==rows[i-1][j-1]+(ref[i-1]!=hyp[j-1]):
            if ref[i-1]!=hyp[j-1]: edits.append({'op':'substitution','reference':ref[i-1],'hypothesis':hyp[j-1]})
            i-=1;j-=1
        elif i and rows[i][j]==rows[i-1][j]+1:
            edits.append({'op':'deletion','reference':ref[i-1],'hypothesis':''});i-=1
        else:
            edits.append({'op':'insertion','reference':'','hypothesis':hyp[j-1]});j-=1
    return rows[-1][-1],list(reversed(edits))


def score(reference,hypothesis):
    ref,hyp=normalize(reference),normalize(hypothesis)
    words=ref.split(); distance,edits=alignment(words,hyp.split())
    cer,_=alignment(list(ref),list(hyp))
    return {'wer':distance/len(words) if words else None,'cer':cer/len(ref) if ref else None,
            'reference_words':len(words),'word_errors':distance,
            'substitutions':sum(x['op']=='substitution' for x in edits),
            'deletions':sum(x['op']=='deletion' for x in edits),
            'insertions':sum(x['op']=='insertion' for x in edits),'edits':edits,
            'terms':{t:normalize(t) in hyp for t in ['BOM index','Nexus','Clockify','ICC1c']}}


def score_terms(reference,hypothesis,terms):
    """Count complete normalized token sequences, including repeated and absent terms."""
    ref,hyp=normalize(reference).split(),normalize(hypothesis).split()
    def count(words,phrase):
        return sum(words[i:i+len(phrase)]==phrase for i in range(len(words)-len(phrase)+1))
    result={}
    for term in terms:
        phrase=normalize(term).split()
        if not phrase:raise ValueError('A term must contain letters or numbers.')
        expected,actual=count(ref,phrase),count(hyp,phrase)
        result[term]={'reference':expected,'hypothesis':actual,'missing':max(0,expected-actual),'extra':max(0,actual-expected)}
    return result


def no_speech_metrics(hypothesis,duration_seconds):
    if duration_seconds<=0:raise ValueError('Audio duration must be positive.')
    words=len(normalize(hypothesis).split())
    return {'nonempty':bool(hypothesis.strip()),'words':words,'words_per_minute':words*60/duration_seconds}
