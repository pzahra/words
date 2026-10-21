# The spreadsheet codec checked by hand against the ODBC text driver (WordsEdit/SPEC.md,
# Spreadsheets, Other tools). Nothing runs it: it is kept for reference.
#
#   odbc-check.ps1 read <folder> <sheet>    reads a sheet Wordsmith exported by its section of the
#                                           folder's schema.ini, and prints each row as the driver
#                                           gives it, a cell with no value <null>
#   odbc-check.ps1 write <folder> <sheet>   has the driver make a sheet of its own, and its section,
#                                           for Wordsmith to import; the sheet must not exist yet
#
# Jet's text driver is 32-bit, so run it under the 32-bit Windows PowerShell:
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File odbc-check.ps1 read C:\out Out.csv
# A 64-bit PowerShell needs the Access Database Engine (ACE) installed, and its driver named:
#   -Driver 'Microsoft Access Text Driver (*.txt, *.csv)'
param(
	[Parameter(Mandatory)][ValidateSet('read', 'write')][string]$Mode,
	[Parameter(Mandatory)][string]$Folder,
	[Parameter(Mandatory)][string]$Sheet,
	[string]$Driver = 'Microsoft Text Driver (*.txt; *.csv)'
)

$connection = New-Object System.Data.Odbc.OdbcConnection("Driver={$Driver};Dbq=$Folder;Extensions=asc,csv,tab,txt;ReadOnly=$(if ($Mode -eq 'write') { 0 } else { 1 });")
$connection.Open()
try {
	$command = $connection.CreateCommand()
	if ($Mode -eq 'read') {
		$command.CommandText = "SELECT * FROM [$Sheet]"
		$reader = $command.ExecuteReader()
		"columns: " + ((0..($reader.FieldCount - 1) | ForEach-Object { "$($reader.GetName($_)) $($reader.GetDataTypeName($_))" }) -join ', ')
		while ($reader.Read()) {
			$cells = 0..($reader.FieldCount - 1) | ForEach-Object {
				if ($reader.IsDBNull($_)) { '<null>' }
				else {
					$text = [string]$reader.GetValue($_)
					if ($text.Length -gt 40) { "[$($text.Length) chars: $($text.Substring(0, 12))...]" }
					else { '[' + ($text -replace "`r", '\r' -replace "`n", '\n') + ']' }
				}
			}
			'  ' + ($cells -join ' | ')
			#the console may not show a character as the sheet's encoding gave it, so a cell past ASCII shows its code points too
			foreach ($i in 0..($reader.FieldCount - 1)) {
				if (-not $reader.IsDBNull($i) -and ([string]$reader.GetValue($i)) -match '[^\x00-\x7E]') {
					"    $($reader.GetName($i)): " + ((([string]$reader.GetValue($i)).ToCharArray() | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' ')
				}
			}
		}
		$reader.Close()
	}
	else {
		#a column of Wordsmith's roles, and one of its own (Notes), which Wordsmith reads past
		$command.CommandText = "CREATE TABLE [$Sheet] ([key] TEXT(50), [value] MEMO, [value-fr] MEMO, [Notes] TEXT(50))"
		[void]$command.ExecuteNonQuery()
		$rows = @(
			@('greeting', 'Hello, "you"', 'Bonjour', 'mine'),
			@('lines', "one`r`ntwo", 'un', ''),
			@('plain', 'Plain', '', '')
		)
		foreach ($row in $rows) {
			$command.CommandText = "INSERT INTO [$Sheet] VALUES (?, ?, ?, ?)"
			$command.Parameters.Clear()
			foreach ($cell in $row) {
				[void]$command.Parameters.AddWithValue('p', $(if ($cell -eq '') { [DBNull]::Value } else { $cell }))
			}
			[void]$command.ExecuteNonQuery()
		}
		"written: $(Join-Path $Folder $Sheet), and its section of $(Join-Path $Folder 'schema.ini')"
	}
}
finally {
	$connection.Close()
}
