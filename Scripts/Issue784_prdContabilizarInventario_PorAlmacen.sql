-- NestoAPI#787 (épica #784, corte 0): prdContabilizarInventario solo toca el almacén de @Delegación y no contabiliza
-- nada si el usuario tiene filas pendientes en otro almacén.
--
-- Por qué: el 22/11/25 el traspaso 77263, lanzado para NUEVAVISION\RDS2016$ al contabilizar el inventario, se llevó a la
-- vez 64 filas de ALC y 25 de REI que estaban con StockReal 0 por el INSERT inicial de las tiendas: el SP contabilizaba
-- TODAS las filas pendientes del usuario y @Delegación solo iba al extracto. Dejó el stock de esas filas a cero.
--
-- Cambios (misma firma: Ariadna vieja y los scripts de siempre siguen valiendo):
--   1. Al principio, antes de tocar nada (RAISERROR y return -1):
--        - el usuario no tiene filas pendientes (estado 1, NºTraspaso NULL) en el almacén;
--        - (solo si @Delegación no es un almacén) el usuario tiene filas pendientes en varios almacenes: no se sabe cuál quiere.
--   2. Parámetro NUEVO y opcional @Almacén (misma firma para Nesto viejo y los scripts de siempre, que pasan cuatro). Con él (la
--      API y Nesto nuevo) se contabiliza ese almacén y SOLO ese; lo pendiente en otros se queda para otra llamada (Carlos 03/11/26:
--      «en cada llamada se contabilice lo que se quiere»). Sin él, el único almacén con filas pendientes del usuario; con varios, nada.
--      @Delegación es la delegación del extracto y NO se usa como almacén (REVISIÓN 03/11/26 16:10: la primera versión exigía que
--      @Delegación fuese un almacén y a Santiago le saltó «El almacén VAR no existe en la empresa 1» desde Nesto viejo).
--   3. Filtran además por almacén = @Almacen: la comprobación de filas sin pasillo, el INSERT en #Ubicacion, el DELETE de
--      Ubicaciones del 29/11/22 (que además ahora cruza el almacén de la ubicación con el de la fila), el INSERT en
--      PreExtrProducto y el UPDATE final de Inventarios.
--   4. @Delegación sigue yendo al extracto como hasta ahora.
-- El resto es el cuerpo vigente en producción (modificado por última vez el 24/11/2025).
--
-- Lanzar como sa. El SP está creado con ANSI_NULLS ON y QUOTED_IDENTIFIER OFF: se mantienen.
-- Al final hay una prueba comentada (BEGIN TRAN ... ROLLBACK).
USE NV
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER OFF
GO
ALTER PROCEDURE [dbo].[prdContabilizarInventario] @Empresa as char(3), @Usuario as varchar(30), @Delegación as char(3), @FormaVenta as char(3), @AlmacenAContabilizar as char(3) = NULL AS
/************************************************************************************************************************************************************************************
 * prdContabilizarInventario - Carlos Adrián Martínez -> 12/11/02											*
 * --------------------------------------------------------------------------------------------------											*
 *																		*
 * Crea los movimientos correspondientes a la tabla Inventarios en PreExtrProducto y luego los contabiliza, dejando regularizados los stocks de los	*
 * productos en esa empresa tal y cómo se inventariaron en el recuento físico.									*
 *																		*
 * Entrada:	@Empresa	-> Empresa en la que se van a regularizar las existencias								*
 * 		@Usuario	-> Usuario del que se van a contabilizar sus movimientos.								*
 * 		@Delegación	-> Delegación del extracto (no es el almacén).											*
 * 		@Almacén	-> (opcional, 03/11/26) Almacén que se contabiliza. Sin él, el único con filas pendientes del usuario.		*
 *																		*
 * Carlos 03/11/26 (NestoAPI#787, épica #784): hasta ahora se contabilizaban TODAS las filas pendientes (estado 1) del usuario, fuesen del almacén	*
 * que fuesen: @Delegación solo iba al extracto y @Almacen se sacaba de la primera fila. El 22/11/25 el traspaso 77263, lanzado para			*
 * NUEVAVISION\RDS2016$ al contabilizar el inventario, se llevó a la vez 64 filas de ALC y 25 de REI que estaban con StockReal 0 por el INSERT	*
 * inicial de las tiendas, y dejó su stock a cero. Desde ahora:												*
 *   - @Almacen es el parámetro @Almacén si viene; si no (Nesto viejo), el único almacén con filas pendientes del usuario.			*
 *   - Con almacén explícito se contabiliza solo ese; lo pendiente en otros se queda. Con 'VAR' y varios almacenes, nada.			*
 *   - Todas las sentencias filtran además por almacén = @Almacen. @Delegación sigue yendo al extracto.						*
 ************************************************************************************************************************************************************************************/
-- Declaración de variables
declare @Exito as bit
declare @Resultado as int
declare @NumTraspaso as int
declare @ProductoUbicacion as char(10)
set @ProductoUbicacion='12375'
-- Inicialización de variables
set @Exito = 1 --> Sin errores
declare @Almacen as char(3)
declare @Mensaje as varchar(2000)
declare @OtrosAlmacenes as varchar(1000)
-- Carlos 03/11/26 (NestoAPI#787): el almacén que se contabiliza. Antes se deducía de la primera fila pendiente del usuario y se
-- contabilizaban TODAS sus filas, fuesen del almacén que fuesen (lo del 22/11/25). Ahora cada llamada contabiliza SOLO un almacén.
-- La delegación (@Delegación) es otra cosa: va al extracto y no se toca (Carlos 03/11/26: «no se puede convertir una delegación en almacén»).
--   * Con @AlmacenAContabilizar (parámetro nuevo, opcional; «@Almacén» chocaría con la variable @Almacen por la intercalación; lo pasa la API desde #790 y Nesto nuevo elige el almacén): ese, y solo ese. Las filas
--     pendientes que el usuario tenga en otros almacenes se quedan como están, para contabilizarlas en otra llamada.
--   * Sin @Almacén (Nesto viejo y los scripts de siempre llaman con cuatro parámetros): el único almacén en el que el usuario tiene
--     filas pendientes. Si las tiene en varios no se puede saber cuál quiere y no se contabiliza nada: hay que hacerlo desde Nesto
--     nuevo (o Ariadna), eligiendo el almacén.
if @AlmacenAContabilizar is not null begin
	if not exists (select * from almacenes where empresa = @Empresa and número = @AlmacenAContabilizar) begin
		set @Mensaje = 'El almacén ' + rtrim(@AlmacenAContabilizar) + ' no existe en la empresa ' + isnull(rtrim(@Empresa), '(nula)')
		raiserror('%s',11,1,@Mensaje)
		return -1
	end
	set @Almacen = @AlmacenAContabilizar
end
else begin
	select @OtrosAlmacenes = isnull(@OtrosAlmacenes + ', ', '') + convert(varchar(10), count(*)) + ' en ' + rtrim(almacén)
		from inventarios
		where empresa = @Empresa and usuario = @Usuario and estado = 1 and nºtraspaso is null
		group by almacén
		order by almacén
	if (select count(distinct almacén) from inventarios where empresa = @Empresa and usuario = @Usuario and estado = 1 and nºtraspaso is null) > 1 begin
		set @Mensaje = 'El usuario ' + @Usuario + ' tiene filas pendientes en varios almacenes (' + @OtrosAlmacenes + '): contabilice cada almacén por separado desde Nesto nuevo o Ariadna. No se ha contabilizado nada'
		raiserror('%s',11,1,@Mensaje)
		return -1
	end
	set @Almacen = (select top 1 almacén from inventarios where empresa = @Empresa and usuario = @Usuario and estado = 1 and nºtraspaso is null)
end

-- el usuario tiene que tener filas pendientes en ese almacén
if @Almacen is null or not exists (select * from inventarios where empresa = @Empresa and usuario = @Usuario and almacén = @Almacen and estado = 1 and nºtraspaso is null) begin
	set @Mensaje = 'El usuario ' + isnull(@Usuario, '(nulo)') + ' no tiene filas pendientes de contabilizar' + isnull(' en ' + rtrim(@Almacen), '')
	raiserror('%s',11,1,@Mensaje)
	return -1
end

-- si el almacen es de los que tiene control de ubicaciones borramos las lineas
-- que se hayan quedado con el pasillo a null
declare @ControlUbicaciones as bit
set @ControlUbicaciones=0
set @ControlUbicaciones=(select ControlUbicaciones from almacenes where empresa=@empresa and numero=@almacen)
if @@error != 0 begin
	raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
	return -1
end
if @controlubicaciones!=0 begin
	-- si tiene control de ubicaciones, borramos
	-- los que el pasillo sea nulo
	if exists (
		select * from  inventarios as i inner join productos as p  on p.empresa=i.empresa and p.numero=i.numero
			where i.empresa =@empresa and i.usuario =@usuario and i.almacén = @Almacen and i.estado = 1  and i.pasillo is null and p.ubicar=1 -- Carlos 03/11/26: solo @Almacen
		) begin
			raiserror('Hay movimientos sin pasillo. Contabilice primero los inventarios sin control de ubicaciones',11,1)
			return -1
		end
	
	-- David Sanchez lopez.. 21/06/04
	-- si estamos haciendo un inventario
	-- tendremos que poner la cantidad en la ubicacion
	-- lo haremos con una tabla temporal, ya que estamos haciendolo con un sum agrupado
	if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#Ubicacion') ) drop table [dbo].[#Ubicacion]
	--despues la creamos
	CREATE TABLE [#Ubicacion] ([Empresa] [char] (3) not null,
	[Almacen] [char] (3),
	[Producto] [char] (15) not null,
	[Pasillo] [char] (3) not null,
	[Fila] [char] (3) not null,	
	[Columna] [char] (3) not null,
	[Cantidad] [int] not null default(0),
	[CantidadReal] [int] not null default (0)
	) ON [PRIMARY]
	-- insertamos en la temporal, un sum agrupando por pasillo, fila, columna...
	insert into #ubicacion (empresa,almacen,producto,pasillo,fila,columna,cantidad,cantidadReal)
		select @empresa,a.número,i.Número,i.pasillo,i.fila,i.columna,sum(i.stockReal-i.stockCalculado),sum(i.stockReal)
			from inventarios as i inner join almacenes as a
				on i.empresa=a.empresa and i.almacen=a.número 
				inner join productos as p
				on i.empresa=p.empresa and i.numero=p.numero
				where p.ubicar=1 and i.empresa = @Empresa and i.usuario = @Usuario and i.almacén = @Almacen and i.estado = 1 and i.stockcalculado<>stockreal and i.nºtraspaso is null -- Carlos 03/11/26: solo @Almacen
				group by a.número,i.número,i.pasillo,i.fila,i.columna
	if @@error != 0 begin
		raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
		return -1
	end
end -- cierra al de si el control de ubicaciones es 1
begin transaction -- COMIENZO TRANSACCIÓN ------------------------------------------------------------------------------------------------------------------
-- Aumentamos el contador de Traspasos almacén
update contadoresGlobales set TraspasoAlmacén = TraspasoAlmacén + 1
select @NumTraspaso = TraspasoAlmacén from ContadoresGlobales
-- David Sanchez Lopez... 21/06/04
-- tenemos que insertar en la tabla ubicaciones 
-- si no existe insertamos, si existiera modificamos la cantidad y le ponemos esta
-- tenemos que insertar tambien una linea en estado -1 con nº de traspaso
if @controlubicaciones!=0 begin
	-- de los que ya existen tenemos que modificar la cantidad
	update ubicaciones set cantidad=t.cantidadReal 
	from #ubicacion as t inner join ubicaciones as u
	on t.almacen=u.almacen and t.producto=u.numero and t.pasillo=u.pasillo and t.fila=u.fila and t.columna=u.columna and u.estado=0
	--on t.empresa=u.empresa and t.almacen=u.almacen and t.producto=u.numero and t.pasillo=u.pasillo and t.fila=u.fila and t.columna=u.columna and u.estado=0
	if @@error != 0 begin
		raiserror ('No se han podido modificar  las ubicaciones', 11, 1)
		set @Exito = 0
		goto Fin
	end
	-- Insertamos las lineas que no existen
	insert into ubicaciones (empresa,almacen,número,cantidad,pasillo,fila,columna,estado)
		select @empresa,t.almacen,t.producto,t.cantidadReal,t.pasillo,t.fila,t.columna,0
		from #ubicacion as t left join ubicaciones as u
		on t.almacen=u.almacen and t.producto=u.numero and t.pasillo=u.pasillo and t.fila=u.fila and t.columna=u.columna and u.estado=0
		--on t.empresa=u.empresa and t.almacen=u.almacen and t.producto=u.numero and t.pasillo=u.pasillo and t.fila=u.fila and t.columna=u.columna and u.estado=0
		where u.pasillo is null
	if @@error != 0 begin
		raiserror ('No se han podido insertar las ubicaciones', 11, 1)
		set @Exito = 0
		goto Fin
	end
	-- Insertamos la linea en estado -1
	insert into ubicaciones (Empresa,Almacén,Número,Cantidad,Pasillo,Fila,Columna,Estado,NºTraspasoInv)
		select empresa,almacen,producto,cantidad,pasillo,fila,columna,-1,@numTraspaso
			from #ubicacion
	if @@error != 0 begin
		raiserror ('No se han podido insertar las ubicaciones', 11, 1)
		set @Exito = 0
		goto Fin
	end
	-- borramos la ubicaciones que se hayan quedado a con cantidad cero y q sean positivas
	delete ubicaciones where cantidad=0 and estado>=0
	if @@error != 0 begin
		raiserror ('No se han podido insertar las ubicaciones', 11, 1)
		set @Exito = 0
		goto Fin
	end

	-- Carlos 29/11/22: borramos las ubicaciones que en el inventario se hayan puesto a cero
	delete Ubicaciones
	from Ubicaciones u
	where estado = 0 and exists (
		select * 
		from inventarios i
		where empresa = @Empresa and usuario = @Usuario and estado = 1 and stockreal = 0 and nºtraspaso is null
		and i.Número = u.Número and i.Pasillo = u.Pasillo and i.Fila = u.Fila and i.Columna = u.Columna
		and i.Almacén = @Almacen and u.Almacén = i.Almacén -- Carlos 03/11/26: solo @Almacen (antes tampoco se cruzaba el almacén de la ubicación)
	)
end 
-- Metemos las líneas en PreExtrProducto
insert into preextrproducto (Empresa, Diario, Número, Fecha, Texto, Almacén, Grupo, Cantidad, [Asiento Automático], Delegación, [Forma Venta], NºTraspaso,pasillo,fila,columna) select @Empresa, '_RegInvent', Número, Fecha, 'Regularización Inventario', Almacén, Grupo, StockReal - StockCalculado, 1, @Delegación, @FormaVenta, @NumTraspaso,pasillo,fila,columna from inventarios where empresa = @Empresa and usuario = @Usuario and almacén = @Almacen and estado = 1 and stockcalculado<>stockreal and nºtraspaso is null -- Carlos 03/11/26: solo @Almacen
if @@error != 0 begin
	raiserror ('No se han podido contabilizar las líneas', 11, 1)
	set @Exito = 0
	goto Fin
end
-- Contabilizamos PreExtrProducto
exec @Resultado = prdExtrProducto @Empresa, '_RegInvent'
if @Resultado < 0 begin
	raiserror ('No se han podido contabilizar las líneas de producto', 11, 1)
	set @Exito = 0
	goto Fin
end
-- Actualizamos la tabla Inventarios para pasar a estado 2, poner traspaso y valorar el movimiento
/*
update inventarios set 
	Estado = 2,
	NºTraspaso = @NumTraspaso,
	Valor = (select PrecioMedio from productos where Productos.empresa = @Empresa and Productos.Número = Inventarios.Número) * StockReal
where empresa = @Empresa and usuario = @Usuario and estado = 1
*/
-- david 14/11/03.. no multiplico para q en el informe me lo ponga bien y no me ponga un cero.
/*select 
	numero, (select isnull(PrecioMedio,0) from productos where Productos.empresa = @Empresa and Productos.Número = Inventarios.Número) from Inventarios
where empresa = @Empresa and usuario = @Usuario and estado = 1 and nºtraspaso is null 
*/

update inventarios set 
	Estado = 2,
	NºTraspaso = @NumTraspaso,
	Valor = (select isnull(PrecioMedio,0) from productos where Productos.empresa = @Empresa and Productos.Número = Inventarios.Número) 
where empresa = @Empresa and usuario = @Usuario and almacén = @Almacen and estado = 1 and nºtraspaso is null -- Carlos 03/11/26: solo @Almacen
if @@error != 0 begin
	raiserror ('No se han podido actualizar los estados de las líneas', 11, 1)
	set @Exito = 0
	goto Fin
end
Fin: -- Finalizamos el procedimiento
if @Exito != 1 begin
	rollback transaction
	return -1
end else begin
	commit transaction -- FIN DE LA TRANSACCIÓN ------------------------------------------------------------------------------------------------------------------------
	return 1
end
GO

/* =====================================================================================================================
   PRUEBA (no toca nada: todo dentro de BEGIN TRAN ... ROLLBACK). Seleccionar desde BEGIN TRAN hasta el último SELECT y
   ejecutar de una vez (mientras dura bloquea ContadoresGlobales; son segundos). Usa REI y ALC, que no llevan control de
   ubicaciones, y un usuario ficticio. Si el contabilizar del paso 4 diese error de fechas, ejecutarlo como un usuario con
   fechas permitidas de hoy (prdExtrProducto las comprueba).

BEGIN TRAN

DECLARE @Usu varchar(30) = 'PRUEBA\Issue787'
DECLARE @Prod char(15), @Grupo char(3), @Subgrupo char(3), @Familia char(10), @r int, @Traspaso int

SELECT TOP 1 @Prod = Número, @Grupo = Grupo, @Subgrupo = Subgrupo, @Familia = Familia
	FROM Productos WHERE Empresa = '1' AND Ubicar = 0 AND Grupo IS NOT NULL AND Subgrupo IS NOT NULL ORDER BY Número DESC

-- 1. Dos filas pendientes del usuario ficticio, una en REI y otra en ALC (StockReal = StockCalculado + 1)
INSERT INTO Inventarios (Empresa, Almacén, Fecha, Número, Grupo, Subgrupo, Familia, StockCalculado, StockReal, Estado, Valor, Aplicacion, Usuario)
	VALUES ('1', 'REI', GETDATE(), @Prod, @Grupo, @Subgrupo, @Familia, 0, 1, 1, 0, 'Prueba787', @Usu),
	       ('1', 'ALC', GETDATE(), @Prod, @Grupo, @Subgrupo, @Familia, 0, 1, 1, 0, 'Prueba787', @Usu)

-- 2. Con pendientes en dos almacenes NO contabiliza: «... tiene 1 filas pendientes en ALC: contabilice cada almacén por separado...»
BEGIN TRY
	EXEC @r = prdContabilizarInventario '1', @Usu, 'REI', 'VAR'
	SELECT 'NO ESPERADO: ha contabilizado' AS Paso2, @r AS Resultado
END TRY
BEGIN CATCH
	SELECT 'OK: ha fallado' AS Paso2, ERROR_MESSAGE() AS Mensaje
END CATCH

-- 3. Almacén que no existe y almacén sin pendientes del usuario: también fallan
BEGIN TRY
	EXEC @r = prdContabilizarInventario '1', @Usu, 'XXX', 'VAR'
	SELECT 'NO ESPERADO' AS Paso3a
END TRY
BEGIN CATCH
	SELECT 'OK: ha fallado' AS Paso3a, ERROR_MESSAGE() AS Mensaje
END CATCH
BEGIN TRY
	EXEC @r = prdContabilizarInventario '1', @Usu, 'ALG', 'VAR'
	SELECT 'NO ESPERADO' AS Paso3b
END TRY
BEGIN CATCH
	SELECT 'OK: ha fallado' AS Paso3b, ERROR_MESSAGE() AS Mensaje
END CATCH

-- Nada ha cambiado: las dos filas siguen en estado 1 sin traspaso
SELECT 'Tras los fallos' AS Momento, Almacén, Estado, NºTraspaso, StockCalculado, StockReal FROM Inventarios WHERE Usuario = @Usu

-- 4. Quitamos la de ALC y contabilizamos REI: solo toca REI
DELETE Inventarios WHERE Usuario = @Usu AND Almacén = 'ALC'
INSERT INTO Inventarios (Empresa, Almacén, Fecha, Número, Grupo, Subgrupo, Familia, StockCalculado, StockReal, Estado, Valor, Aplicacion, Usuario)
	VALUES ('1', 'ALC', GETDATE(), @Prod, @Grupo, @Subgrupo, @Familia, 0, 1, 2, 0, 'Prueba787', @Usu) -- ya contabilizada (estado 2): no estorba ni se toca
EXEC @r = prdContabilizarInventario '1', @Usu, 'REI', 'VAR'
SELECT @Traspaso = MAX(NºTraspaso) FROM Inventarios WHERE Usuario = @Usu AND Almacén = 'REI'
SELECT 'Paso4' AS Paso, @r AS Resultado_esperado_1, @Traspaso AS Traspaso
-- REI en estado 2 con el traspaso y Valor = PrecioMedio; ALC sigue en estado 2 sin traspaso
SELECT 'Inventarios' AS Tabla, Almacén, Estado, NºTraspaso, Valor FROM Inventarios WHERE Usuario = @Usu
-- El extracto del traspaso: UNA fila, de REI, +1, diario _RegInvent, delegación REI
SELECT 'ExtractoProducto' AS Tabla, Almacén, Número, Cantidad, Diario, Delegación, [Forma Venta], NºTraspaso
	FROM ExtractoProducto WHERE Empresa = '1' AND NºTraspaso = @Traspaso

ROLLBACK
SELECT @@TRANCOUNT AS TransaccionesAbiertas_esperado_0
   ===================================================================================================================== */
