import sys

if(len(sys.argv) < 2):
    print("usage : " + sys.argv[0] + " [input]")
    sys.exit()


inF = open(sys.argv[1],'r')
outFa = open(sys.argv[1]+ 'A.h', 'w')
outFb = open(sys.argv[1]+ 'B.h', 'w')


while True:
    line = inF.readline()
    if not line:
        break

    
    a = line[:-1].split(' ')
    if len(a) == 1:
        outFa.write('\\\n')
        outFb.write('\n')
        continue

    fn = sys.argv[1].split('\\')[-1:]
    #style X-macro
    outFa.write("X(" + a[0] + ", " + fn[0].split('.')[-2:-1][0].upper() + "_" + a[1].upper() + ")\\\n")
    #style switch
    outFb.write("case " + a[0] + ": return \"" + a[1] + "\";\n")


inF.close()
outFa.close()
outFb.close()
