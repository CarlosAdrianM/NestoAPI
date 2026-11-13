/*
    Tope de memoria de SQL Server (13/11/2026). Lanzar como sa. Se aplica al momento, sin reiniciar SQL Server.

    Por qué: el servidor de la base de datos tiene 8 GB y SQL Server estaba sin tope («max server memory» = 2147483647),
    así que se queda con casi toda la memoria y no deja sitio al propio Windows ni al instalador de actualizaciones.
    Con 5632 MB (5,5 GB) quedan unos 2,5 GB para el sistema.

    Para volver a como estaba: mismo comando con 2147483647.
*/
EXEC sp_configure 'show advanced options', 1;
RECONFIGURE;
EXEC sp_configure 'max server memory (MB)', 5632;
RECONFIGURE;

SELECT name, CAST(value_in_use AS int) AS ValorEnUso
FROM sys.configurations
WHERE name = 'max server memory (MB)';
