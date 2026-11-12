/*
    12/11/26 (Carlos, cliente 27625 contacto 2): al guardar la ficha comercial (PUT api/Clientes/ClienteComercial), cambiando solo
    vendedor y estado, el disparador trgClientesUpd lo rechaza con «Lo que el usuario intenta hacer borraria la direccion del cliente».

    Causa: la comprobacion «se deja la direccion vacia» solo mira que la direccion ANTERIOR no sea NULL. El contacto 2 del 27625 ya
    tenia la direccion EN BLANCO (50 espacios, no NULL) y la API guarda la fila entera (EntityState.Modified), asi que Direccion va
    en el SET aunque no cambie: «antes no NULL, ahora en blanco» → error. Ese contacto no se podia modificar de ninguna manera.

    Arreglo: solo es «borrar la direccion» si la anterior TENIA algo. Se cambia, en las dos consultas de esa comprobacion,
        where d.direccion is not null and (i.direccion is null ...
    por
        where d.direccion is not null and ltrim(d.direccion) <> '' and (i.direccion is null ...
    Lo demas del disparador queda igual (se reescribe su propio texto con ese unico cambio).

    Como sa en NV. Se puede lanzar entero y repetir: si ya esta aplicado, lo dice y no hace nada.
*/
USE NV;
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @def nvarchar(max) = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.trgClientesUpd'));
DECLARE @viejo nvarchar(200) = N'where d.direccion is not null and (i.direccion is null';
DECLARE @nuevo nvarchar(200) = N'where d.direccion is not null and ltrim(d.direccion) <> '''' and (i.direccion is null';

IF @def IS NULL
BEGIN
    RAISERROR(N'No encuentro el disparador dbo.trgClientesUpd. No se ha cambiado nada.', 16, 1);
    RETURN;
END

DECLARE @vecesNuevo int = (LEN(@def) - LEN(REPLACE(@def, @nuevo, N''))) / LEN(@nuevo);
DECLARE @vecesViejo int = (LEN(@def) - LEN(REPLACE(@def, @viejo, N''))) / LEN(@viejo);

IF @vecesNuevo = 2 AND @vecesViejo = 0
BEGIN
    PRINT N'Ya estaba aplicado: el disparador ya exige que la direccion anterior tuviera algo. No se cambia nada.';
    RETURN;
END
IF @vecesViejo <> 2 OR @vecesNuevo <> 0
BEGIN
    RAISERROR(N'El disparador no esta como se esperaba (la comprobacion deberia salir 2 veces; sale %d, y %d ya cambiadas). No se ha cambiado nada.', 16, 1, @vecesViejo, @vecesNuevo);
    RETURN;
END

DECLARE @cabecera int = PATINDEX(N'%CREATE TRIGGER%', @def);
IF @cabecera = 0
BEGIN
    RAISERROR(N'No encuentro «CREATE TRIGGER» en el texto del disparador. No se ha cambiado nada.', 16, 1);
    RETURN;
END

SET @def = REPLACE(@def, @viejo, @nuevo);
SET @def = STUFF(@def, @cabecera, LEN(N'CREATE TRIGGER'), N'ALTER TRIGGER');

EXEC sys.sp_executesql @def;
PRINT N'trgClientesUpd cambiado: solo es «borrar la direccion» si la anterior tenia algo.';
GO

-- Comprobacion: tiene que salir 2.
SELECT (LEN(definition) - LEN(REPLACE(definition, N'ltrim(d.direccion) <> '''' and (i.direccion is null', N''))) / LEN(N'ltrim(d.direccion) <> '''' and (i.direccion is null') AS VecesCambiado
FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.trgClientesUpd');
GO
