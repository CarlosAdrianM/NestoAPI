/*
    trgClientesUpd - que la comprobacion de CIF/NIF duplicado solo salte si el NIF CAMBIA.

    ⚠️ LA VERSION ANTERIOR DE ESTE FICHERO NO HACIA NADA. Llevaba el cambio escrito dentro de un
    bloque de comentario, como "antes/despues", y lo unico ejecutable era el SELECT de comprobacion.
    Se ejecuto el 08/09/26 a las 17:09 y, logicamente, siguio dando 26. Esta version SI aplica.

    EL SINTOMA
    ----------
    08/09/26, ELMAH: "Ya existe un cliente con ese CIF/NIF. La transaccion termino en el
    desencadenador." en PUT /api/Clientes (GestorClientes.ModificarCliente). El usuario estaba
    metiendo un CCC en la ficha: no habia tocado el NIF.

    POR QUE PASA
    ------------
    1. La API guarda con `db.Entry(cliente).State = EntityState.Modified`, y eso hace que Entity
       Framework escriba TODAS las columnas del cliente, tambien [CIF/NIF], aunque no cambie.
    2. El disparador usa `if update([CIF/NIF])`, que en T-SQL es cierto cuando la columna esta EN LA
       LISTA del UPDATE, cambie de valor o no.

    Resultado: la comprobacion se ejecuta SIEMPRE desde la API, y como salta si otro numero de
    cliente de alta tiene ese mismo NIF, un cliente duplicado en los datos queda con la ficha
    CONGELADA: no se le puede cambiar la direccion, ni el telefono, ni anadirle un CCC. Al escribir
    esto hay 26 numeros de cliente asi (empresa 1, los dos de alta), casi todos duplicados reales:
    "DOLORES MARIA GARCIA VILLEGAS" y "DOLORES M.a GARCIA VILLEGAS" con el mismo NIF.

    EL ARREGLO
    ----------
    Comparar `inserted` contra `deleted` y actuar solo si el NIF cambia de verdad. Es EXACTAMENTE la
    condicion que ya tiene la comprobacion de al lado (la del NIF de mas de 9 caracteres, unas lineas
    mas arriba en el mismo disparador): se corrige una incoherencia interna, no se inventa nada.

    Lo que NO cambia: si alguien intenta poner un NIF que ya tiene otro cliente de alta, sigue
    saltando igual que siempre.

    COMO ESTA HECHO, Y POR QUE ASI
    ------------------------------
    El disparador tiene 553 lineas y aqui solo cambia una. En vez de pegar el cuerpo entero (que
    obligaria a reproducirlo a mano, con el riesgo de colar una errata en produccion), el script LEE
    la definicion actual de sys.sql_modules, comprueba que el texto viejo aparece EXACTAMENTE UNA
    VEZ, lo sustituye y aplica el resultado. Si el disparador se ha tocado y ese texto ya no esta
    igual, el script ABORTA sin cambiar nada en vez de adivinar.

    COMPLEMENTO PENDIENTE (no lo hace este script)
    ----------------------------------------------
    Que la API deje de reescribir columnas que no cambian (`EntityState.Modified` en
    GestorClientes.ModificarCliente). Eso quitaria ruido a TODOS los disparadores de la tabla.

    COMO SE EJECUTA
    ---------------
    DDL sobre un disparador de produccion: desde SSMS con `sa`.
*/

USE NV;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @definicion nvarchar(max);
DECLARE @viejo      nvarchar(max);
DECLARE @nuevo      nvarchar(max);
DECLARE @apariciones int;
DECLARE @crlf       nchar(2) = CHAR(13) + CHAR(10);

SELECT @definicion = m.definition
FROM sys.sql_modules m
JOIN sys.triggers t ON t.object_id = m.object_id
WHERE t.name = 'trgClientesUpd';

IF @definicion IS NULL
BEGIN
    RAISERROR('No se encuentra el disparador trgClientesUpd. Se aborta.', 16, 1);
    RETURN;
END

-- El texto EXACTO que hay hoy (volcado de produccion el 08/09/26). Ojo al tabulador inicial y al
-- doble espacio de "and  empresa": va tal cual.
SET @viejo = N'	if (select top 1 [nº cliente] from inserted where [CIF/NIF] in (select [CIF/NIF] from clientes where estado>=0 and  empresa = inserted.empresa and [nº cliente] != inserted.[nº cliente])) is not null begin';

SET @apariciones = (DATALENGTH(@definicion) - DATALENGTH(REPLACE(@definicion, @viejo, N''))) / DATALENGTH(@viejo);

IF @apariciones <> 1
BEGIN
    RAISERROR('El texto a sustituir aparece %d veces (se esperaba 1): el disparador ha cambiado. Se aborta sin tocar nada.', 16, 1, @apariciones);
    RETURN;
END

SET @nuevo =
      N'	-- Carlos, 08/09/26: antes bastaba con que [CIF/NIF] fuera en el SET del UPDATE, y la API' + @crlf
    + N'	-- guarda el cliente entero (EntityState.Modified), asi que esto saltaba en CUALQUIER' + @crlf
    + N'	-- modificacion de la ficha aunque el NIF no cambiara: 26 clientes con NIF duplicado se' + @crlf
    + N'	-- quedaban sin poder tocar ni la direccion ni un CCC. Ahora se compara inserted contra' + @crlf
    + N'	-- deleted, igual que ya hace la comprobacion del NIF largo de aqui arriba.' + @crlf
    + N'	if exists (' + @crlf
    + N'			select 1' + @crlf
    + N'			from inserted as i' + @crlf
    + N'			inner join deleted as d' + @crlf
    + N'				on  d.empresa      = i.empresa' + @crlf
    + N'				and d.[nº cliente] = i.[nº cliente]' + @crlf
    + N'				and d.contacto     = i.contacto' + @crlf
    + N'			where isnull(i.[CIF/NIF], '''') <> isnull(d.[CIF/NIF], '''')' + @crlf
    + N'			  and exists (select 1 from clientes c' + @crlf
    + N'			              where c.estado >= 0' + @crlf
    + N'			                and c.empresa = i.empresa' + @crlf
    + N'			                and c.[nº cliente] != i.[nº cliente]' + @crlf
    + N'			                and c.[CIF/NIF] = i.[CIF/NIF])' + @crlf
    + N'	) begin';

SET @definicion = REPLACE(@definicion, @viejo, @nuevo);

-- CREATE -> ALTER. La cadena lleva el nombre del disparador, asi que solo casa con la cabecera.
IF CHARINDEX(N'CREATE TRIGGER [dbo].[trgClientesUpd]', @definicion) <> 1
BEGIN
    RAISERROR('La definicion no empieza por CREATE TRIGGER [dbo].[trgClientesUpd]. Se aborta.', 16, 1);
    RETURN;
END

SET @definicion = STUFF(@definicion, 1, LEN(N'CREATE TRIGGER [dbo].[trgClientesUpd]'),
                        N'ALTER TRIGGER [dbo].[trgClientesUpd]');

BEGIN TRANSACTION;

EXEC sp_executesql @definicion;

-- Verificacion: el disparador tiene que haber quedado con la condicion nueva y sin la vieja.
DECLARE @resultado nvarchar(max);
SELECT @resultado = m.definition
FROM sys.sql_modules m
JOIN sys.triggers t ON t.object_id = m.object_id
WHERE t.name = 'trgClientesUpd';

IF CHARINDEX(N'where isnull(i.[CIF/NIF], '''') <> isnull(d.[CIF/NIF], '''')' + @crlf + N'			  and exists (select 1 from clientes c', @resultado) = 0
   OR CHARINDEX(@viejo, @resultado) <> 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('El disparador no ha quedado como se esperaba: ROLLBACK.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

PRINT 'trgClientesUpd actualizado: la comprobacion de CIF/NIF duplicado ya solo salta si el NIF cambia.';
GO

/*
    QUE HAY QUE COMPROBAR DESPUES (a mano, no lo hace el script)
    -----------------------------------------------------------
    1. Editar uno de los clientes con NIF duplicado (p. ej. anadirle un CCC) y ver que GUARDA.
       Antes daba "Ya existe un cliente con ese CIF/NIF".
    2. Intentar poner en un cliente el NIF de OTRO cliente de alta y ver que SIGUE dando el error.
       Esa es la funcion del disparador y no debe haberse perdido.

    Los 26 clientes con NIF duplicado siguen estando duplicados: esto no arregla los datos, solo
    deja de bloquear la edicion de sus fichas. Si se quieren fusionar, es otra tarea.
*/

SET NOCOUNT ON;
SELECT 'Clientes con NIF duplicado (siguen existiendo, ahora ya editables) = '
     + CAST(COUNT(*) AS varchar)
FROM (SELECT DISTINCT c.[Nº Cliente]
      FROM Clientes c
      WHERE c.Empresa = '1' AND c.Estado >= 0
        AND ISNULL(c.[CIF/NIF], '') <> ''
        AND EXISTS (SELECT 1 FROM Clientes o
                    WHERE o.Empresa = c.Empresa AND o.Estado >= 0
                      AND o.[CIF/NIF] = c.[CIF/NIF]
                      AND o.[Nº Cliente] <> c.[Nº Cliente])) t;
GO
