drag a file into OpcodeScrript.py
it makes two type codes

$$$$example for "A"$$$$

#define DSTOPLIST \
	X(0, DSTOP_TRUE)\
	X(1, DSTOP_SBARFOLDER)\
	X(2, DSTOP_SBARSONG)\
	X(3, DSTOP_SBARCOURSE)\
	X(4, DSTOP_SBARCREATECOURSE)\
	X(5, DSTOP_PLAYABLE)\
\
\
	X(10, DSTOP_DOUBLEorDOUBLEBATTLE)\
	X(11, DSTOP_BATTLE)\
	X(12, DSTOP_BATTLEorDOUBLEBATTLEorBATTLE)\
	X(13, DSTOP_GBATTLEorBaTTLE)\

#define X(N,S) S = N,
typedef enum DSTOP {
	DSTOPLIST
}DSTOP;
#undef X

#define X(N,S) dstOPname[N].assign(#S);
static const auto dstOPname = []() {
	CSTR dstOPname;
	DSTOPTLIST
		return dstOPname;
}();
#undef X



$$$$example for "B"$$$$

const char* dstName(unsigned n)
{
  switch(n) {
    case 0: return "AlwaysTrue";
    case 1: return "FolderBarIsASong";
    default: return "<Unknown>";
  }
}