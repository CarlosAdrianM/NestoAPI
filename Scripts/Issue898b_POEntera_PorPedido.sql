/*
    NestoAPI#898 (Carlos 26/11/26): el bloqueo «una P.O. se factura entera o no se factura» pasa a ser POR PEDIDO.
    Hasta ahora (Issue898_FinDeMesNestoViejo_y_POEntera.sql) solo miraba los pedidos de clientes marcados con P.O. obligatoria
    (dbo.ClientesSuPedidoObligatorio, hoy solo el 27593). Desde este script: CUALQUIER pedido con «Su pedido» relleno.

    Ojo: «Su pedido» lleva a veces un texto que no es una P.O. («ENSEÑAR A SANTY», «ESPATULA DE CAMBIO EN GARANTIA»: 3 de 37
    pedidos en 90 días). Ese pedido tampoco se podrá facturar a medias; si hace falta, se vacía el campo.

    Lanzar como sa en la BD NV, después del script anterior. Se puede lanzar más de una vez. Si el procedimiento no tiene el
    bloque esperado, no cambia nada y lo dice. Apagar todo el bloqueo: 'Facturacion.POSoloEntera' = '0' en «(defecto)».
*/
USE NV;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @def nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.prdCrearFacturaVta'));
DECLARE @cab nvarchar(200) = N'CREATE   PROCEDURE [dbo].[prdCrearFacturaVta]';
DECLARE @vieja nvarchar(400) = N'	if isnull(@po898, '''') <> '''' and exists (select 1 from ClientesSuPedidoObligatorio o where o.Empresa = @emp898 and o.Cliente = @cliente898) begin';
DECLARE @nueva nvarchar(400) = N'	-- NestoAPI#898-POPorPedido (Carlos 26/11/26): por pedido, no por cliente: basta con que el pedido lleve «Su pedido».
	if isnull(@po898, '''') <> '''' begin';
IF @def IS NULL
    RAISERROR('No existe dbo.prdCrearFacturaVta o no se puede leer su definición. No se ha cambiado nada.', 16, 1);
ELSE IF CHARINDEX(N'NestoAPI#898-POPorPedido', @def) > 0
    PRINT 'dbo.prdCrearFacturaVta: ya estaba aplicado (NestoAPI#898-POPorPedido). No se toca.';
ELSE IF CHARINDEX(N'NestoAPI#898-POEntera', @def) = 0
    RAISERROR('dbo.prdCrearFacturaVta no tiene el bloque NestoAPI#898-POEntera: lance antes Issue898_FinDeMesNestoViejo_y_POEntera.sql. No se ha cambiado nada.', 16, 1);
ELSE IF CHARINDEX(@cab, @def) = 0 OR (LEN(@def) - LEN(REPLACE(@def, @vieja, N''))) / LEN(@vieja) <> 1
    RAISERROR('dbo.prdCrearFacturaVta no tiene la cabecera o la condición esperadas. No se ha cambiado nada: avise.', 16, 1);
ELSE
BEGIN
    SET @def = STUFF(@def, CHARINDEX(@cab, @def), LEN(@cab), REPLACE(@cab, N'CREATE', N'ALTER'));
    SET @def = REPLACE(@def, @vieja, @nueva);
    EXEC sys.sp_executesql @def;
    PRINT 'dbo.prdCrearFacturaVta: aplicado (NestoAPI#898-POPorPedido).';
END
GO
SELECT OBJECT_NAME(object_id) AS Procedimiento, LEN(definition) AS Largo, uses_ansi_nulls, uses_quoted_identifier,
       CASE WHEN definition LIKE N'%NestoAPI#898-POPorPedido%' THEN 'por pedido' WHEN definition LIKE N'%NestoAPI#898-POEntera%' THEN 'por cliente marcado' ELSE 'SIN APLICAR' END AS Estado
FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.prdCrearFacturaVta');
GO
