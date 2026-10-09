#include "En_dbio.h"

// TODO: drop this helper
int SQL_Run(CSTR queryStr, sqlite3 *sql) {
	return sqlite3_exec(sql, queryStr.body, nullptr, nullptr, nullptr);
}

// TODO: drop this helper
int SQL_prepare(CSTR queryStr, sqlite3 *sql, sqlite3_stmt **ppStmt) {
	return sqlite3_prepare(sql, queryStr.body, -1, ppStmt, nullptr);
}

CSTR SQL_GetColumn(int i, sqlite3_stmt *pStmt){
	if (sqlite3_column_type(pStmt, i) == SQLITE_NULL) {
		return {};
	}

	// sqlite3_column_bytes(pStmt, i);

	// Cast safety: it's safe to cast from unsigned char* to char*
	const char* columnText = reinterpret_cast<const char*>(sqlite3_column_text(pStmt, i));

	return columnText;
}
