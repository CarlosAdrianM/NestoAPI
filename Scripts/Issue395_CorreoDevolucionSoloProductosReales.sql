-- =============================================================================
-- NestoAPI#395: el correo "Informe de Devolucion de Producto" solo para devoluciones reales
--
-- El SP disparaba el aviso (y la exigencia de comentario obligatorio, con rollback) con
-- CUALQUIER linea de cantidad < 0. Con datos de los ultimos ~7.800 albaranes:
--   871 lineas  tipolinea 1 + producto NO ficticio  -> devoluciones de verdad (siguen avisando)
--    82 lineas  tipolinea 1 + producto ficticio     -> TiCKET "Suscribete y ahorra"  (dejan de avisar)
--   169 lineas  tipolinea 2 + no esta en Productos  -> DESCUENTO PORTES 624...       (dejan de avisar)
-- No hay ni un solo caso de producto real con tipolinea distinta de 1, asi que el filtro no
-- silencia ninguna devolucion legitima.
--
-- IMPORTANTE: el SP original esta compilado con QUOTED_IDENTIFIER OFF y ANSI_NULLS ON
-- (sys.sql_modules: uses_quoted_identifier=0, uses_ansi_nulls=1). Los SET de abajo lo
-- respetan: recrearlo con otros valores cambia el comportamiento del SP.
-- =============================================================================
USE NV
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER OFF
GO
ALTER PROCEDURE [dbo].[prdCrearAlbaránVta] @Empresa char(3),@Pedido int,@FechaEntrega datetime,@ImporteMínimo as money, @Usuario varchar(30) = NULL  AS
/*
Códigos de Return:
-3: Importe del albarán menor a importe mínimo (09/07/02)
*/
/*
if system_user!='NUEVAVISION\Carlos' begin
	raiserror('No disponible. Intenta más tarde',11,1)
	return -2 -- sale del procedimiento con error
end
*/
declare @Fecha datetime
set @Fecha = getdate()
declare @ProductoUbicacion as char(10)
set @ProductoUbicacion=''
declare @Espejo as char(3)
set @espejo=(select [iva por defecto] from empresas where número=@empresa)

-- Carlos 04/03/25: si no pasan el parámetro usuario ponemos el del sistema
if @Usuario is null OR CHARINDEX('$', SUSER_NAME()) = 0 begin
	set @Usuario = SYSTEM_USER
end

-- Carlos Adrián Martínez - 07/04/22
-- Evitamos que las líneas pendientes de base imponible = 0 las meta en carpeta
if (
	exists(select * from LinPedidoVta where Empresa = @Empresa and Número = @Pedido and Recoger != 0 and [Base Imponible] = 0 and Estado = 1)
	and not exists(select * from LinPedidoVta where Empresa = @Empresa and Número = @Pedido and Recoger != 0 and [Base Imponible] !=0)
	and exists(select * from LinPedidoVta where Empresa = @Empresa and Número = @Pedido and [Base Imponible] != 0)
	)
	begin
	update LinPedidoVta set Estado = -1, Recoger = 0 where Empresa = @Empresa and Número = @Pedido and Recoger != 0 and [Base Imponible] = 0 and Estado = 1
end

-- Carlos 11/11/21: creamos una tabla temporal para poder leer de ahí
select *
into #LinPedidoVta
from LinPedidoVta where Empresa = @Empresa and Número = @Pedido

-- Carlos 13/09/17: guardamos en una variable si alguna línea tiene picking
-- Carlos 11/11/21 refactorizo
declare @ningunaLineaTienePicking as bit = 1
if exists (select * from #LinPedidoVta where estado = 1 and [fecha entrega]<=@fechaentrega and Picking > 0) begin
	set @ningunaLineaTienePicking = 0
end
/*
if (select top 1 Empresa from #LinPedidoVta where empresa=@empresa and número = @Pedido and estado = 1 and [fecha entrega]<=@fechaentrega and Picking > 0) is not null begin
	set @ningunaLineaTienePicking = 0
end
*/

-- Carlos 21/06/23: no se puede albaranear algo sin picking en un pedido que tiene algo con picking
if exists (select * from #LinPedidoVta where Picking > 0) 
	and exists (select * from #LinPedidoVta where estado = 1 and [fecha entrega]<=@fechaentrega and Picking = 0) begin
	raiserror('No se puede crear albarán de líneas sin picking en un pedido que tiene líneas con picking',11,1)
	return -2 -- sale del procedimiento con error
end


-- Comprobar si hay líneas para albaranear
-- Carlos 11/11/21 refactorizo
if not exists (select * from #LinPedidoVta where estado = 1 and [fecha entrega]<=@fechaentrega) begin
	raiserror('No hay líneas para albaranear',11,1)
	return -2 -- sale del procedimiento con error
end
/*
if (select count(*) from linpedidovta where empresa=@empresa and número = @Pedido and estado = 1 and [fecha entrega]<=@fechaentrega) < 1 begin
	raiserror('No hay líneas para albaranear',11,1)
	return -2 -- sale del procedimiento con error
end
*/


-- Carlos 31/03/15: comprobar si hay líneas sin tipo y dar error
-- Carlos 11/11/21: refactorizo
if exists (select * from #LinPedidoVta where estado = 1 and [fecha entrega]<=@fechaentrega and TipoLinea is null) begin
	raiserror('No se puede crear albarán de líneas sin tipo',11,1)
	return -2 -- sale del procedimiento con error
end
/*
if (select count(*) from linpedidovta where empresa=@empresa and número = @Pedido and estado = 1 and [fecha entrega]<=@fechaentrega and TipoLinea is null) > 0 begin
	raiserror('No se puede crear albarán de líneas sin tipo',11,1)
	return -2 -- sale del procedimiento con error
end
*/

-- David Sanchez 14/04/05... tenemos que comprobar que el pedido no sea una nota de entrega, ya que si lo es, no puede crear un albaran
-- Carlos 11/11/21: refactorizo
if exists (
		select * 
		from cabpedidovta as c inner join #LinPedidoVta as l
		on c.empresa=l.empresa and c.numero=l.numero
		where c.notaentrega=1 and l.yafacturado=0
	) begin
	raiserror('El pedido es nota de entrega',11,1)
	return -2 -- sale del procedimiento con error
end

/*
if (select count(*) from cabpedidovta as c inner join linpedidovta as l
	on c.empresa=l.empresa and c.numero=l.numero
	where c.empresa=@empresa  and c.notaentrega=1 and c.numero=@Pedido  and l.yafacturado=0)>0 begin
	raiserror('El pedido es nota de entrega',11,1)
	return -2 -- sale del procedimiento con error
end
*/

-- David Sanchez 15/11/07 ... compruebo que no haya lineas en -1
-- Carlos 11/11/21: refactorizo
if exists (
		select * 
		from #LinPedidoVta as l inner join productos as p
		on l.empresa=p.empresa and l.producto=p.numero
		where l.estado=1 and [fecha entrega]<=@fechaentrega and p.estado=-1
	) begin
	raiserror('No se puede albaranear porque hay productos anulados',11,1)
	return -2 -- sale del procedimiento con error
end

/*
if (select count(*)  from linpedidovta as l inner join productos as p
on l.empresa=p.empresa and l.producto=p.numero
where l.empresa=@Empresa and l.numero=@Pedido and l.estado=1 and [fecha entrega]<=@fechaentrega  and p.estado=-1)>0 begin
	raiserror('No se puede albaranear porque hay productos anulados',11,1)
	return -2 -- sale del procedimiento con error

end
*/




-- David Sanchez 02/02/05 .. tenemos que juntar las ofertas
declare @DevueltoJuntar as int
set @devueltojuntar=0
exec @devueltoJuntar= prdAgruparOfertasPedido @empresa,@Pedido
if @devueltoJuntar<0 begin
	raiserror('No se pudo albaranear',11,1)
	return -2 -- sale del procedimiento con error
end 

-- David Sanchez ... 29/08/08 ... Estaba al crear la factura pero lo cambio al crear el albaran porque Alfredo dice que es mejor y se lo ha dicho a Carlos.
-- Buscamos si hay productos que necesitan el numero de serie.
-- Carlos 11/11/21 refactorizo
if exists (
		select * 
		from #LinPedidoVta as l inner join productos as p 
		on l.empresa=p.empresa and l.producto=p.numero
		where l.estado = 1 and l.[fecha entrega]<=@fechaentrega	and p.NecesitaNumSerie=1 and l.numserie is null
	) begin	
	raiserror('Hay productos que necesitan el número de serie en las líneas.',11,1)
	return -2 -- sale del procedimiento con error
end
/*
if (select count(l.producto) from linpedidovta as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
	where l. empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega
	and p.NecesitaNumSerie=1 and l.numserie is null)>=1 begin
	
	raiserror('Hay productos que necesitan el número de serie en las líneas.',11,1)
	return -2 -- sale del procedimiento con error
end
*/

-- David sanchez... 12-05-08 ... buscamos si hay productos que necesiten numero de serie y aunque lo
-- tengan puesto, lo tienen mal metido. 
/*
if (select count(l.producto) from linpedidovta as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
	where  l. empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega
	and p.NecesitaNumSerie=1)>=1 begin


	if (select count(l.producto)  from linpedidovta as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
		left join numerosserie as n on (l.producto=n.producto or n.producto is null) 
			and (l.numserie=n.numserie or n.numserie is null)
		where   l. empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega
			and p.NecesitaNumSerie=1 and n.numserie is null)>=1 begin

		raiserror('Hay productos que el número de serie no corresponde con ninguno.',11,1)
		return -2 -- sale del procedimiento con error
	end

end 
*/
 
-- David Sanchez Lopez 10/02/04... comprobamos las lienas ya facturadas, y si hubiera alguna no dejamos albaranear
-- Carlos 11/11/21: refactorizo
if exists (
	select * from #LinPedidoVta 
	where estado = 1 and [fecha entrega]<=@fechaentrega and yafacturado=1
) begin
	raiserror('El pedido tiene lineas ya facturadas, tiene que crear la nota de entrega',11,1)
	return -2 -- sale del procedimiento con error
end
/*
if (select count(*) from linpedidovta where empresa=@empresa and número = @Pedido and estado = 1 and [fecha entrega]<=@fechaentrega and yafacturado=1) >0 begin
	raiserror('El pedido tiene lineas ya facturadas, tiene que crear la nota de entrega',11,1)
	return -2 -- sale del procedimiento con error
end
*/
-- David Sanchez Lopez... 05/09/06... Tenemos que ver si hay lineas que sean lineaparcial=1 entre las que vamos a albaranear, y si las hay
-- ponemos el importe minimo a cero, ya que esas estan pendientes por culpa nuestra (no habia stock) y estas no tienen que entrar.
-- Carlos 11/11/21: refactorizo
if exists (
	select * 
	from #LinPedidoVta 
	where estado = 1 and [fecha entrega]<=@fechaentrega and lineaparcial=1
) begin
	set @ImporteMínimo=0
end

-- David Sanchez 23/12/04... Tenemos que comprobar que no haya ninguna linea con picking y que no este reservada
/**************
27/12/04: CARLOS ¡¡¡ OJO!!! ARREGLAR ESTO, HE TENIDO QUE PONER UN FILTRO CHAPUZA DE PRODUCTO LIKE 'B%' PORQUE DABA ERROR
EN TODAS LAS PACTURAS CON BONIFICACIÓN
Linea original:
if (
select count(*)  from cabpedidovta as c inner join linpedidovta as l 
on c.empresa=l.empresa and c.numero=l.numero
where c.notaentrega=0 and l.picking>0 and l.empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and l.[nº orden] not in (select nºordenvta from ubicaciones where estado=3))>=1 begin
	raiserror('Hay lineas de picking sin ubicacion reservada.',11,1)
	return -2 -- sale del procedimiento con error
end
******************************************************************/
/*
if (
select count(*)  from cabpedidovta as c inner join linpedidovta as l 
on c.empresa=l.empresa and c.numero=l.numero
where c.notaentrega=0 and l.cantidad-l.recoger>0 and  l.picking>0 and l.empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and ( l.producto not like 'B%'  and  l.producto not like 'R%')  and l.tipolinea = 1 and l.[nº orden] not in (select nºordenvta from ubicaciones where estado=3))>=1 begin
	raiserror('Hay lineas de picking sin ubicacion reservada.',11,1)
	return -2 -- sale del procedimiento con error
end
*/
-- lo pongo con un join porque si no tarda mucho
-- Carlos 11/11/21: refactorizo
declare @productoSinUbicacionReservada as char(15) = (
	select top 1 producto 
	from #LinPedidoVta as l inner join cabpedidovta as c 
	on l.empresa=c.empresa and c.numero=l.numero
	left join ubicaciones as u
	on (l.empresa=u.empresa or l.empresa is null) and (l.[nº orden]=u.[nºordenvta] or l.[nº orden] is null)
	and (u.estado=3 or u.estado is null)
	where c.notaentrega=0 and l.cantidad-l.recoger>0 and l.picking>0 and l.estado=1 and l.[fecha entrega]<=@fechaentrega and l.tipolinea=1
	and u.numero is null
)
/*
declare @productoSinUbicacionReservada as char(15) = (
select top 1 producto from linpedidovta as l 
inner join cabpedidovta as c on l.empresa=c.empresa and c.numero=l.numero
left join ubicaciones as u
on (l.empresa=u.empresa or l.empresa is null) and (l.[nº orden]=u.[nºordenvta] or l.[nº orden] is null)
and (u.estado=3 or u.estado is null)
where c.notaentrega=0 and l.cantidad-l.recoger>0 and l.picking>0 and l.empresa=@empresa and  l.numero=@pedido and l.estado=1 and l.[fecha entrega]<=@fechaentrega and l.tipolinea=1
and u.numero is null
)
*/
if @productoSinUbicacionReservada is not null begin
	set @productoSinUbicacionReservada = RTRIM(ltrim(@productoSinUbicacionReservada))
	raiserror('Hay lineas de picking sin ubicacion reservada. Producto %s',11,1, @productoSinUbicacionReservada)
	return -2 -- sale del procedimiento con error
end

-- lo he puesto en la restriccion de la tabla
/*
-- David Sanchez Lopez 15/09/03
-- tenemos que comprobar que no hay lineas que el bruto no sea igual que el precio*cantidad
if (select count(número) from linpedidovta where  número = @Pedido and estado = 1  and empresa=@empresa and bruto<>precio*cantidad and tipolinea=1) >0 begin
	raiserror('Hay lineas que el bruto o el precio no coincide',11,1)
	return -2 -- sale del procedimiento con error
end
*/
-- miramos si tiene control de ubicaciones
-- 07/04/05... David Sanchez... tenemos que comprobar el stock no reservado que quedaria en el picking
-- Carlos 11/11/21: refactorizo
declare @hayControlUbicaciones as bit = (
	select top 1 a.ControlUbicaciones 
	from #LinPedidoVta as l inner join almacenes as a 
	on l.empresa=a.empresa and l.almacen=a.número
	where l.estado = 1 and l.[fecha entrega]<=@fechaentrega 
)
if @hayControlUbicaciones !=0 begin
--if (select top 1 a.controlubicaciones from linpedidovta as l inner join almacenes as a on l.empresa=a.empresa and l.almacen=a.número 	where  l.empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega )!=0 begin
	if @@error != 0 begin
		raiserror('No se ha podido determinar el stock',11,1)
		return -1
	end
	if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#StockNeg') ) drop table [dbo].[#StockNeg]
	--	despues la creamos
	CREATE TABLE [#StockNeg] ([Producto] char(15),Almacen char (3),[stock] int not null default (0),Cantidad int not null default (0)) ON [PRIMARY]
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		return -1
	end
	-- Carlos 11/11/21: refactorizo
	insert into #StockNeg (producto,almacen,cantidad)
	select producto,almacen,sum(cantidad-recoger) 
	from #LinPedidoVta 
	where estado = 1 and [fecha entrega]<=@fechaentrega and picking<=0 and tipolinea=1
	group by producto,almacen
	/*
	insert into #StockNeg (producto,almacen,cantidad)
		select producto,almacen,sum(cantidad-recoger) from linpedidovta 
			where empresa=@empresa and número = @Pedido and estado = 1 and [fecha entrega]<=@fechaentrega and picking<=0 and tipolinea=1
				group by producto,almacen
	*/
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		return -1
	end
		
	if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#PuenteStockneg') ) drop table [dbo].[#PuenteStockNeg]
	--	despues la creamos
	CREATE TABLE [#PuenteStockNeg] ([Producto] char(15),Almacen char (3),Cantidad int not null default (0)) ON [PRIMARY]
		insert into #PuenteStockNeg (producto,almacen,cantidad)
			select l.producto,l.almacen,sum(u.cantidad) from #stockneg as l inner join 
				ubicaciones as u on l.producto=u.numero and l.almacen=u.almacen
				where u.estado between 0 and 2 
				group by l.producto,l.almacen
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		return -1
	end
	update #stockNeg set stock=p.cantidad from #PuenteStockNeg as p inner join #stockNeg as s
		on p.producto=s.producto and p.almacen=s.almacen
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		return -1
	end
	delete #PuentestockNeg
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		return -1
	end
	delete #stockNeg from   #stockNeg inner join productos as p on
		#stockNeg.producto=p.número where p.ficticio=1
	if @@error!=0 begin
			

		raiserror('No se pudieron comprobar los productos ficticios.',16,1)
		return -1
	end
	declare @ref as char(15)
	set @ref=(select top 1 producto from #Stockneg where stock-cantidad<0)
	if @ref is not null begin
		-- David Sanchez Lopez... 11/03/04
		-- Busco el nombre del producto 
		declare @Nombre as char(50)
		set @nombre=(Select nombre from productos where empresa=@empresa and número=@ref)		
		-- Pongo esto para que me coja el rtrim. Despues del print salta el error pero no lo muestra en pantalla, solo muestra el print
		print 'El producto '+rtrim(@ref)+' ('+ltrim(rtrim(@Nombre))+') se quedaria en stock negativo'
		raiserror('El producto %s se quedaría en stock negativo.',16,1, @ref)
		return -1
	end 
end
-- Comprobar Importe Mínimo
-- [CARLOS] 09/07/02
-- Crear cabecera de albarán y actualizar líneas pedido
declare @UltNumAlbarán int,@NumCliente char(10),@Contacto char(3),@Vendedor char(3),@PeriodoFacturación as char(3),@MotivoDevolución as char(3)
select @UltNumAlbarán = Albaranes from ContadoresGlobales
select @NumCliente = [nº cliente],@contacto = Contacto,@Vendedor = Vendedor,@PeriodoFacturación = [Periodo Facturación], @MotivoDevolución = MotivoDevolución from CabPedidoVta where empresa=@empresa and número = @pedido

-- Carlos 22/05/14
declare @CodigoPostal as char(5)
select @CodigoPostal = CodPostal from clientes where empresa = @Empresa and [Nº Cliente] = @NumCliente and contacto = @Contacto
if @CodigoPostal is null begin
	raiserror('No se puede crear el albarán sin código postal (c/ %s)',11, 1, @NumCliente)
	rollback
	return -1
end

-- COMIENZA TRANSACCIÓN -------------------------------------------------------------------------------------------------------------------
begin transaction

-- Carlos Adrián Martínez 02/07/15. Cambiamos a otros aparatos los productos que no lleven el margen suficiente
exec prdCambiarGrupoPorMargen @Pedido
if @@error != 0 begin
	raiserror('No se ha podido ajustar los grupos de comisión',11,1)
	rollback
	return -1
end

-- Carlos Adrián Martínez 23/02/17. Guardamos los vendedores por grupo de producto
exec prdActualizarComisionesPorGrupoProducto @Empresa, @Pedido
if @@error != 0 begin
	raiserror('No se han podido guardar los vendedores por grupo de producto',11,1)
	rollback
	return -1
end

/*
-- Carlos 13/09/17: quitamos el importe mínimo porque hace update de picking y otras cosas que se controlan en otros sitios
if @ImporteMínimo <> 0 begin -- el importe mínimo 0 se considera como sin importe mínimo
	declare @ImporteAlbarán as money
	select @ImporteAlbarán = sum([Base Imponible]) from linpedidovta where empresa=@empresa and número = @Pedido and (estado >= 1 and estado <=2) and [fecha entrega]<=@fechaentrega
	if @@error != 0 begin
		raiserror('No se ha podido calcular el importe minímo',11,1)
		rollback
		return -1
	end
		-- pongo el estado 2 también en la select porque también se va a facturar y al fin y al cabo es el importe de la factura lo que cuenta
	if @ImporteAlbarán < @ImporteMínimo begin
		-- David Sanchez... 29/11/04.. antes de poner las lineas del picking a cero, tenemos que llamar a deshacer ubicacion picking
		-- para ello lo hacemos con un bucle
		declare @Picking as int
		declare @DevueltoDeshacerPicking as int
		declare crsPicking cursor local fast_forward for select [nº orden ] from linpedidovta  where  empresa=@empresa and número = @Pedido and estado = 1 and [fecha entrega]<=@fechaentrega and (picking!=0 and picking is not null)
		if @@error != 0 begin
			raiserror('No se ha podido calcular el importe minímo',11,1)
			rollback
			return -1
		end
		open crsPicking
		if @@error != 0 begin
			raiserror('No se ha podido calcular el importe minímo',11,1)
			rollback
			return -1
		end
		fetch next from crsPicking into @Picking
		while @@fetch_status=0 begin
			exec @devueltoDeshacerPicking= prdDeshacerUbicacionPicking @empresa,@Pedido,@Picking,0
			If @devueltodeshacerpicking<0 begin
				raiserror('No se pudo deshacer el picking',1,1)
				rollback
				return -3 -- sale del procedimiento con error -3 específico para designar que el importe es insuficiente	
			end
		fetch next from crsPicking into @Picking
		end
		close crsPicking
		deallocate crsPicking
		-- pongo el pedido como pendiente cuando no llega al importe mínimo ([CARLOS] 03/09/02)
		update linpedidovta set picking = 0,estado=-1 where empresa=@empresa and número = @Pedido and estado = 1 and [fecha entrega]<=@fechaentrega
		if @@error != 0 begin
			raiserror('No se ha podido calcular el importe minímo',11,1)
			rollback
			return -1
		end
		-- salgo del procedimiento sin crear el albarán
		raiserror('Importe del albarán es menor al importe mínimo',1,1)
		-- validamos la transacion
		commit transaction
		return -3 -- sale del procedimiento con error -3 específico para designar que el importe es insuficiente
	end
end
*/
update ContadoresGlobales set Albaranes = @UltNumAlbarán + 1
if @@error != 0 begin
	raiserror('No se ha podido actualizar el contador de albaranes',11,1)
	rollback
	return -1
end
-- David Sanchez Lopez... 18/05/04
-- Si el almacen en el que estamos tiene control ubicaciones
-- lo hacemos
declare @Ubicacion as int
-- Carlos 11/11/21 refactorizo
/*
if (select top 1 a.controlubicaciones from linpedidovta as l inner join almacenes as a on l.empresa=a.empresa and l.almacen=a.número
	where  l.empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega )!=0 begin
*/
if @hayControlUbicaciones !=0 begin
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		rollback
		return -1
	end
	-- tendremos que buscar si hay algun producto que tenga mas de una ubicacion, y si fuera asi no le dejaremos 
	-- filtramos por que el pedido sea nulo
	-- filtramos tambien por que el picking de la linea sea menor o igual que cero, para que las que ya tengan picking no las tenga en cuenta
	-- Tenemos que filtrar tambien por que no esten en la tabla ubicaciones reservadas, esto es con un left join de ubicaciones reservadas
	-- las que el campo nºordenvta de ubicaciones reservadas sea nulo
	if @espejo is null begin 
		-- Carlos 11/11/21: refactorizo
		if not exists (
			select * from (
				select numero,almacen,count(*) as contador from (
					select u.número,u.almacen,u.pasillo,u.fila,u.columna  
					from ubicaciones as u inner join linpedidovta as l -- no pongo #LinPedidoVta porque no filtra por Empresa y me da miedo
					on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
					left join ubicacionesreservadas as ur
					on l.empresa=ur.empresa and l.numero=ur.numero and l.[nº orden]=ur.[nºordenvta]
					where u.estado=0 and u.pedidovta is null and l.picking<=0 and l.número=@pedido and l.estado=1 and l.[fecha entrega]<=@fechaentrega  and  l.tipolinea=1 
						and (l.cantidad-l.recoger)>0 and ur.[nºordenvta] is null
					and l.producto not in (
						select producto from #LinPedidoVta 
						where estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 
						group by producto 
						having sum(cantidad-recoger)=0
					)
					group by u.número,u.almacen,u.pasillo,u.fila,u.columna
				) as a group by numero,almacen having count(*)>1
			) as b) begin
		/*
		if (select count(*) from (select numero,almacen,count(*) as contador from (
			select u.número,u.almacen,u.pasillo,u.fila,u.columna  from ubicaciones as u inner join linpedidovta as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur
			on l.empresa=ur.empresa and l.numero=ur.numero and l.[nº orden]=ur.[nºordenvta]
			where u.estado=0 and u.pedidovta is null and l.picking<=0 and l.número=@pedido and l.estado=1 and l.[fecha entrega]<=@fechaentrega  and  l.tipolinea=1 and (l.cantidad-l.recoger)>0 and ur.[nºordenvta] is null
			and l.producto not in ((select producto  from linpedidovta where empresa=@empresa and numero=@pedido and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0))
			group by u.número,u.almacen,u.pasillo,u.fila,u.columna) as a group by numero,almacen having count(*)>1) as b)=0  begin
		*/		
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
			set @ubicacion=0
		end else begin
			set @Ubicacion=1
		end
	
	end else begin
		if not exists (
			select * from (
				select numero,almacen,count(*) as contador from (
					select u.número,u.almacen,u.pasillo,u.fila,u.columna  
					from ubicaciones as u inner join linpedidovta as l -- no pongo #LinPedidoVta porque no filtra por Empresa y me da miedo
					on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen 
					left join ubicacionesreservadas as ur
					on l.empresa=ur.empresa and l.numero=ur.numero and l.[nº orden]=ur.[nºordenvta]
					where u.estado=0 and u.pedidovta is null and (u.empresa=@empresa or u.empresa=@espejo) and l.picking<=0 and l.número=@pedido and l.estado=1 and 
						l.[fecha entrega]<=@fechaentrega  and  l.tipolinea=1 and (l.cantidad-l.recoger)>0 and ur.[nºordenvta] is null
						and l.producto not in (
							select producto  from #LinPedidoVta 
							where estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 
							group by producto 
							having sum(cantidad-recoger)=0
						)
					group by u.número,u.almacen,u.pasillo,u.fila,u.columna
				) as a group by numero,almacen having count(*)>1
			) as b
		)  begin
		/*
		if (select count(*) from (select numero,almacen,count(*) as contador from (
			select u.número,u.almacen,u.pasillo,u.fila,u.columna  from ubicaciones as u inner join linpedidovta as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur
			on l.empresa=ur.empresa and l.numero=ur.numero and l.[nº orden]=ur.[nºordenvta]
			where u.estado=0 and u.pedidovta is null and (u.empresa=@empresa or u.empresa=@espejo) and l.picking<=0 and l.número=@pedido and l.estado=1 and l.[fecha entrega]<=@fechaentrega  and  l.tipolinea=1 and (l.cantidad-l.recoger)>0 and ur.[nºordenvta] is null
			and l.producto not in ((select producto  from linpedidovta where empresa=@empresa and numero=@pedido and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0))
			group by u.número,u.almacen,u.pasillo,u.fila,u.columna) as a group by numero,almacen having count(*)>1) as b)=0  begin
		*/
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end		
			set @ubicacion=0
		end else begin
			set @Ubicacion=1
		end
	end
	-- si tiene mas de una ubicacion le mostramos un error 
	if @ubicacion=1 begin
		rollback
		-- el numero de delante l
		raiserror('50001 Hay productos con varias ubicaciones',11,1)
		--raiserror (50001,16,1)
		return -2
	end else begin
		-- David Sanchez 19/05/04... Lo tenemos que hacer con una tabla temporal, ya que lo que hacemos es un update
		-- ponemos tambien el pasillo, la fila y la columna para ver las ubicaciones reservadas
		if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#Ubi') ) drop table [dbo].[#Ubi]
		CREATE TABLE [#Ubi] 
			([empresa] [char] (3) NOT NULL ,
			[Almacen] [char] (3) NOT NULL ,
			[Producto] [char] (15) NOT NULL ,
			[Cantidad]  [integer],
			[Pasillo] [char] (3),
			[Fila] [char] (3),
			[Columna] [char] (3)
		)	
	      	ON [PRIMARY] 
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
		
		-- sumamos todo, indempendiente mente de como sea el sigo, para que me lo coja bien
		-- filtramos por grupos producto para q no filtre por productos bonificaciones
		-- filtramos tambien por el picking, para que no me meta aqui las lineas que tienen picking
		-- David Sanchez Lopez.. 31/05/04
		-- los que esten con un estado negativo no lo inserto, porque no lo sumo hasta q no lo ubique
		-- y lo inserto con estado 2 para que en el stock lo tenga en cuenta
		
		-- primero insertamos las que no estan reservadas con la ubicacion a nulo
		-- Carlos 11/11/21 refactorizo
		if @espejo is null begin  
			insert into #ubi (empresa,almacen,producto,cantidad) 
			select l.empresa,l.almacen,l.producto,-sum(l.cantidad-l.recoger) 
			from #LinPedidoVta as l left join gruposproducto as g
			on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
			left join ubicacionesreservadas as u
			on l.empresa=u.empresa and l.numero=u.numero and l.[nº orden]=u.nºordenvta
			where l.tipolinea=1 and l.picking<=0 and l.estado=1 and l.[fecha entrega]<=@fechaEntrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is null
			group by l.empresa,l.almacen,l.producto 
			/*
			insert into #ubi (empresa,almacen,producto,cantidad) 
				select l.empresa,l.almacen,l.producto,-sum(l.cantidad-l.recoger) from linpedidovta as l left join gruposproducto as g
					on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
					left join ubicacionesreservadas as u
					on l.empresa=u.empresa and l.numero=u.numero and l.[nº orden]=u.nºordenvta
					where l.tipolinea=1 and  l.empresa=@empresa and l.número=@pedido and l.picking<=0 and l.estado=1 and l.[fecha entrega]<=@fechaEntrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is null
						 group by l.empresa,l.almacen,l.producto 
			*/
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end else begin
			-- Carlos 11/11/21 refactorizo
			insert into #ubi (empresa,almacen,producto,cantidad) 
			select l.empresa,l.almacen,l.producto,-sum(l.cantidad-l.recoger) 
			from #LinPedidoVta as l inner join gruposproducto as g
			on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
			left join ubicacionesreservadas as u
			on l.empresa=u.empresa and l.numero=u.numero and l.[nº orden]=u.nºordenvta
			where l.tipolinea=1 and l.picking<=0 and l.estado=1 and l.[fecha entrega]<=@fechaEntrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is null
			group by l.empresa,l.almacen,l.producto 
			/*
			insert into #ubi (empresa,almacen,producto,cantidad) 
				select l.empresa,l.almacen,l.producto,-sum(l.cantidad-l.recoger) from linpedidovta as l inner join gruposproducto as g
					on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
					left join ubicacionesreservadas as u
					on l.empresa=u.empresa and l.numero=u.numero and l.[nº orden]=u.nºordenvta
					where l.tipolinea=1 and l.empresa=@empresa and l.número=@pedido and l.picking<=0 and l.estado=1 and l.[fecha entrega]<=@fechaEntrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is null
						 group by l.empresa,l.almacen,l.producto 
			*/
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end
		if @espejo is null begin
			-- Ponemos el pasillo, la fila y la columna,  de estos que hemos metido
			update #ubi set pasillo=u.pasillo,fila=u.fila,columna=u.columna from ubicaciones as u  inner join #ubi as t
				on u.almacen=t.almacen and u.numero=t.producto where u.estado=0
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end else begin
			-- Ponemos el pasillo, la fila y la columna,  de estos que hemos metido
			update #ubi set pasillo=u.pasillo,fila=u.fila,columna=u.columna from ubicaciones as u  inner join #ubi as t
				on u.empresa=t.empresa and u.almacen=t.almacen and u.numero=t.producto where u.estado=0
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end
		-- a continuacion insertamos las ubicaciones reservadas
		-- Carlos 11/11/21 refactorizo
		insert into #ubi (empresa,almacen,producto,cantidad,pasillo,fila,columna) 
		select l.empresa,l.almacen,l.producto,sum(-u.cantidad),u.pasillo,u.fila,u.columna 
		from #LinPedidoVta as l inner join gruposproducto as g
		on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
		left join ubicacionesreservadas as u
		on l.empresa=u.empresa and l.numero=u.numero and l.[nº orden]=u.nºordenvta
		where l.picking<=0 and l.estado=1 and l.[fecha entrega]<=@fechaEntrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is not null
		group by l.empresa,l.almacen,l.producto,u.pasillo,u.fila,u.columna  
		/*
		insert into #ubi (empresa,almacen,producto,cantidad,pasillo,fila,columna) 
			select l.empresa,l.almacen,l.producto,sum(-u.cantidad),u.pasillo,u.fila,u.columna from linpedidovta as l inner join gruposproducto as g
			on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
			left join ubicacionesreservadas as u
			on l.empresa=u.empresa and l.numero=u.numero and l.[nº orden]=u.nºordenvta
				where l.empresa=@empresa and l.número=@pedido and l.picking<=0 and l.estado=1 and l.[fecha entrega]<=@fechaEntrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is not null
					 group by l.empresa,l.almacen,l.producto,u.pasillo,u.fila,u.columna  
		*/
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end		
		
		-- David Sanchez Lopez...04/05/05 borramos de la tabla temporal los productos con los que este pedido no haria nada
		-- Carlos 11/11/21 refactorizo
		delete #ubi where producto in (
			select producto 
			from #LinPedidoVta 
			where estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 
			group by producto 
			having sum(cantidad-recoger)=0
		)
		--delete #ubi where producto in (select producto  from linpedidovta where empresa=@empresa and numero=@pedido and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0)
		if @@error != 0 begin
			raiserror('Ocurrió un error al ubicar el producto',11,1)
			rollback
			return -1
		end	
		-- De los que se han quedado sin ubicacion, tendriamos que buscar si estan pendientes de ubicar, y si asi fuera, lo tendriamos que ir cogiendo de alli
		
		declare @Producto as char(15)
		declare @Cantidad as integer
		declare @Almacen as char(3)
		
	
		declare @NºOrdenUbicacion as integer
		declare @CantidadUbicacion as integer
		declare @CantidadPendiente as integer
		declare crsUbi cursor local fast_forward for select producto,cantidad,Almacen from #Ubi  where pasillo is null
		if @@Error!=0 begin
			rollback transaction
			raiserror ('No se puede determinar la ubicación',16,1)
			return -1
		end
		open crsUbi
		if @@Error!=0 begin
			rollback transaction
			raiserror ('No se puede determinar la ubicación',16,1)
			return -1
		end
		fetch next from crsUbi into @producto,@cantidad,@almacen
		while @@fetch_status=0 begin
			set @CantidadPendiente=-@cantidad 
	    	
			-- mientras que la cantidad pendiente de asignar sea mayor de cero 
			-- tendremos que hacer esto
			while @CantidadPendiente>0 begin
			
				-- Ponemos sólo las de estado 2
	
				set @NºOrdenUbicacion=0
				select  top 1 @NºOrdenUbicacion= nºorden,@cantidadubicacion=cantidad from ubicaciones where almacen=@almacen and numero=@producto and (empresa=@empresa or empresa=@espejo) and  estado=2 order by fechacreacion asc
				if @@Error!=0 begin
					rollback transaction
					raiserror ('No se puede determinar la ubicación',16,1)
					return -1
				end
				
				if @NºordenUbicacion is not null and @NºordenUbicacion!=0 begin
				
					-- puden pasar 3 opciones
					if @cantidadPendiente=@CantidadUbicacion begin
						-- es que vamos a dar toda esta ubicacion, por lo que modificamos el estado
						-- primero ponemos la empresa del albaran a nulo, ya que vamos a modificar
						
						update ubicaciones set empresaalbaranvta=null,nºordenvta=null where nºorden=@nºordenUbicacion
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End	
						update ubicaciones set cantidad=-cantidad,estado=-1,AlbaranVta=@UltNumAlbaran + 1,PedidoVta=@Pedido where nºorden=@nºordenUbicacion
						
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End	
						
					end else if @CantidadPendiente<@CantidadUbicacion begin
						-- es que vamos a dar menos de la ubicacion, por lo que modificamos la cantidad en la que estamos y hacemos el insert
						update ubicaciones set cantidad=cantidad-@CantidadPendiente where nºorden=@nºordenUbicacion
					
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End	
						
						-- una vez hecho el update tenemos que insertar por la cantidad pendiente	
					
						insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,estado,AlbaranVta,PedidoVta)
							select empresa,almacen,numero,-@cantidadPendiente,pasillo,fila,columna,-1,@UltNumAlbaran + 1,@Pedido from ubicaciones where nºorden=@nºordenUbicacion
					
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End
							
					end else if @CantidadPendiente>@CantidadUbicacion begin
						-- es que vamos a dar todo, pero todavia quedara algo 
						update ubicaciones set empresaalbaranvta=null,nºordenvta=null where nºorden=@nºordenUbicacion
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End	
						update ubicaciones set cantidad=-cantidad,estado=-1,AlbaranVta=@UltNumAlbaran + 1,PedidoVta=@Pedido where nºorden=@nºordenUbicacion
					
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End		
						
					end
							
				
					-- actualizamos la variable cantidad pendiente		
					set @cantidadPendiente=@cantidadpendiente-@cantidadubicacion
				end else begin
					set @CantidadPendiente=0
				end 	
			end
		fetch next from crsUbi into @producto,@cantidad,@almacen
		end
	close crsUbi
	deallocate crsUbi
		-- tendremos que dividir la linea de ubicacion en dos
		-- primer restamos en la que este
		if @espejo is null begin
			update ubicaciones set cantidad=u.cantidad+(l.cantidad) from ubicaciones as u inner join #Ubi as l
				on  u.almacen=l.almacen and u.numero=l.producto and u.pasillo=l.pasillo and u.fila=l.fila and u.columna=l.columna
				where u.estado=0 and u.pedidovta is null 
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end else begin
			update ubicaciones set cantidad=u.cantidad+(l.cantidad) from ubicaciones as u inner join #Ubi as l
				on u.empresa=l.empresa and u.almacen=l.almacen and u.numero=l.producto and u.pasillo=l.pasillo and u.fila=l.fila and u.columna=l.columna
				where u.estado=0 and u.pedidovta is null 
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end
		-- a continuacion insertamos y lo insertamos con estado -1
		-- insertamos solo lo que sea de salida, que ya este ubicado, es decir, que la cantidad sea mayor de cero, porque lo otro ya lo insertamos luego
		-- filtramos por el picking
		-- primero las q	que no estan reservadas
		if @espejo is null begin
			-- Carlos 11/11/21 refactorizo
			insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidovta,albaranvta,estado,NºOrdenVta)
			select l.empresa,l.almacen,l.producto,-1*(l.cantidad-l.recoger),u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nº orden] 
			from ubicaciones as u inner join #LinPedidoVta as l
			on  u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur 
			on l.empresa=ur.empresa and l.numero=ur.numero and  l.[nº orden]=ur.[nºordenvta]
			where u.estado=0 and u.pedidovta is null and l.picking<=0 and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)>0 and ur.nºordenvta is null
			and l.producto not in (
				select producto 
				from #LinPedidoVta 
				where estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 
				group by producto 
				having sum(cantidad-recoger)=0
			)
			/*
			insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidovta,albaranvta,estado,NºOrdenVta)
			select l.empresa,l.almacen,l.producto,-1*(l.cantidad-l.recoger),u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nº orden] from ubicaciones as u inner join linpedidovta as l
			on  u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur 
			on l.empresa=ur.empresa and l.numero=ur.numero and  l.[nº orden]=ur.[nºordenvta]
			where u.estado=0 and u.pedidovta is null and l.picking<=0 and  l.empresa=@empresa and  l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)>0 and ur.nºordenvta is null
			and l.producto not in ((select producto  from linpedidovta where empresa=@empresa and numero=@pedido and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0))
			*/
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end else begin
			-- Carlos 11/11/21 refactorizo
			insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidovta,albaranvta,estado,NºOrdenVta)
			select l.empresa,l.almacen,l.producto,-1*(l.cantidad-l.recoger),u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nº orden] 
			from ubicaciones as u inner join #LinPedidoVta as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur 
			on l.empresa=ur.empresa and l.numero=ur.numero and  l.[nº orden]=ur.[nºordenvta]
			where u.estado=0 and u.pedidovta is null and l.picking<=0 and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)>0 and ur.nºordenvta is null
			and l.producto not in (
				select producto  from #LinPedidoVta where estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0
			)
			/*
			insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidovta,albaranvta,estado,NºOrdenVta)
				select l.empresa,l.almacen,l.producto,-1*(l.cantidad-l.recoger),u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nº orden] from ubicaciones as u inner join linpedidovta as l
				on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
				left join ubicacionesreservadas as ur 
				on l.empresa=ur.empresa and l.numero=ur.numero and  l.[nº orden]=ur.[nºordenvta]
				where u.estado=0 and u.pedidovta is null and l.picking<=0 and  l.empresa=@empresa and  l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)>0 and ur.nºordenvta is null
				and l.producto not in ((select producto  from linpedidovta where empresa=@empresa and numero=@pedido and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0))
			*/
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end
		-- a continuacion las reservadas
		-- Carlos 11/11/21 refactorizo
		insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidovta,albaranvta,estado,NºOrdenVta)
		select l.empresa,almacen,l.producto,-1 * u.cantidad,u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nº orden] 
		from ubicacionesreservadas as u inner join #LinPedidoVta as l
		on u.empresa=l.empresa and u.numero=l.numero and u.nºordenvta=l.[nº orden]
		where  l.picking<=0 and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is not null
		and l.producto not in (
			select producto 
			from #LinPedidoVta 
			where estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 
			group by producto 
			having sum(cantidad-recoger)=0
		)
		/*
		insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidovta,albaranvta,estado,NºOrdenVta)
			select l.empresa,almacen,l.producto,-1 * u.cantidad,u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nº orden] from ubicacionesreservadas as u inner join linpedidovta as l
			on u.empresa=l.empresa and u.numero=l.numero and u.nºordenvta=l.[nº orden]
			where  l.picking<=0 and  l.empresa=@empresa and  l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)>0 and u.nºordenvta is not null
			and l.producto not in ((select producto  from linpedidovta where empresa=@empresa and numero=@pedido and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0))
		*/
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
	end
	
	-- de los que sean entradas (es decir que cantidad - recoger sea <0) tendremos que hacer el insert pero sin una ubicacion
	-- ya que es una entrada en el almacen y lo que queremos es que se le de una ubicacion con ariadna
	-- lo insertamos con estado -2
	-- aqui no filtramos por el picking, ya que estas son entradas y en los picking solo ponemos de salida
	-- David Sanchez 31/05/04.. los insertamos con estado 2, ya que esta en stock, aunque no este todavia ubicado	
	-- David Sanchez 04/05/05... filtramos para que no me tenga en cuenta los productos que agrupando la suma de las cantidades de cero, a exepcion de las lineas que tienen picking.
	-- Carlos 11/11/21 refactorizo
	insert into ubicaciones (empresa,almacen,numero,cantidad,pedidovta,albaranvta,estado,nºordenvta)
	select l.empresa,l.almacen,l.producto,-1*(l.cantidad-l.recoger),l.numero,@UltNumAlbarán+1,2,[nº orden]  
	from #LinPedidoVta as l inner join gruposproducto as g
	on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
	inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
	where l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)<0 and p.ubicar=1
	and l.producto not in (
		select producto  
		from #LinPedidoVta 
		where picking<=0 and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 
		group by producto 
		having sum(cantidad-recoger)=0
	)
	/*
	insert into ubicaciones (empresa,almacen,numero,cantidad,pedidovta,albaranvta,estado,nºordenvta)
		select l.empresa,l.almacen,l.producto,-1*(l.cantidad-l.recoger),l.numero,@UltNumAlbarán+1,2,[nº orden]  from linpedidovta as l inner join gruposproducto as g
			on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
			inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
			where l.empresa=@empresa  and l.número = @Pedido and l.estado = 1 and l.[fecha entrega]<=@fechaentrega and (l.cantidad-l.recoger)<0 and p.ubicar=1
			and l.producto not in (select producto  from linpedidovta where picking<=0 and  empresa=@empresa and numero=@pedido and estado=1 and [fecha entrega]<=@fechaentrega and tipolinea=1 group by producto having sum(cantidad-recoger)=0)
	*/
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		rollback
		return -1
	end
	-- tendremos que cojar las lineas que esten en estado 3 y entren dentro de este albaran, y ponerlas con estado -3 y con la cantidad en negativo
	-- ya que son las del picking y en el momento de hacer el albaran ya salen.
	update  ubicaciones set albaranvta=null,empresaalbaranvta=null from ubicaciones as u inner join linpedidovta as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen and u.nºordenvta=l.[nº orden]
			where  u.estado=3 and  l.número=@pedido and l.estado=1 and l.[fecha entrega]<=@fechaentrega  and  l.tipolinea=1 and l.picking!=0
	if @@error != 0 begin
		raiserror('No se han podido recuperar los productos con picking',11,1)
		rollback
		return -1
	end
	update ubicaciones set albaranvta=@UltNumAlbarán+1,estado=-3,cantidad=u.cantidad*-1  from ubicaciones as u inner join linpedidovta as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen and u.nºordenvta=l.[nº orden]
			where  u.estado=3 and  l.número=@pedido and l.estado=1 and l.[fecha entrega]<=@fechaentrega  and  l.tipolinea=1 and l.picking!=0
		
		if @@error != 0 begin
			raiserror('No se han podido recuperar los productos con picking',11,1)
			rollback
			return -1
		end
	-- borramos las ubicaciones que el estado sea cero y la cantidad sea cero tambien
		delete ubicaciones where estado=0 and cantidad=0
		if @@error != 0 begin
			raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
			rollback
			return -1
		end
end  -- Cierra a lo de si el almacen tiene control de ubicaciones.
insert into CabAlbaránVta (empresa,número,fecha,[nº cliente],contacto,PeriodoFacturación,MotivoDevolución, CodigoPostal, Usuario) 
values (@empresa,@UltNumAlbarán + 1, @fecha,@NumCliente,@Contacto,@PeriodoFacturación, @MotivoDevolución, @CodigoPostal, @Usuario)
if @@error != 0 begin
	raiserror('No se ha podido crear la cabecera de albarán',11,1)
	rollback
	return -1
end

-- david sanchez lopez.. 17/12/03
-- tendremos que insertar las lineas del pedido cuyo campo recoger sea
-- true
-- Primero comprobamos si hay alguna
-- 04/03/05
-- David Sanchez Lopez... esto lo haremos solo de las lineas que no sean de abono. Si hay alguna linea de abono, lo haremos despues 
-- buscamos el numero de contadores globales
declare @Traspaso as int
declare @NumOrdenRecoger as int
declare @CantidadTotal as int
declare @CantidadRecoger as int
declare @CantidadEntregar as int
declare @DevuelveRecoger  as int -- para ver qué me devuelve el prdExtrProducto

-- Carlos 11/11/21 refactorizo
if exists (select * from #LinPedidoVta where estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega and recoger!=0 and cantidad>0) begin
--if (select count(numero) from LinPedidoVta where empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega and recoger!=0 and cantidad>0)>0 begin
	-- David Sanchez...31/03/05  si hay algo tenemos que ver si el pedido e un leasing, ya que si es un leasing, se recogera al
	-- cliente, y al contacto que aparezca en la tabla leasing. Para saber si es un leasing buscamos en la misma tabla
	declare @ClienteLeasing as char(10)
	declare @ContactoLeasing as char(3)
	declare @HayLeasing as bit
	set @HayLeasing=0
	-- buscamos si hay un leasing y si lo hay ponemos la variable a true, y ponemos el cliente y el contacto
	-- para ponerle a el campo recoger
	-- Carlos 11/11/21 refactorizo
	if exists (select * from leasing where pedido=@Pedido and estado=0 and banco=@NumCliente and contactobanco=@Contacto) begin
	--if (select count(*) from leasing where pedido=@Pedido and estado=0 and banco=@NumCliente and contactobanco=@Contacto)>0 begin
		set @HayLeasing=1
		select @clienteLeasing=nºcliente,@contactoleasing=contacto from leasing where pedido=@Pedido and estado=0 and banco=@NumCliente and contactobanco=@Contacto
		if @@error != 0 begin
			raiserror('No se ha podido recoger el producto',11,1)
			rollback
			return -1
		end
	end
	
	
	-- si hay alguna tendremos que hacerlo con un cursor y en base a los numeros de orden
	-- buscamos los que recoger sean mayor a cero
	-- Carlos 11/11/21 refactorizo
	declare crsRecoger cursor local fast_forward for select [nº orden],cantidad,recoger from #LinPedidoVta where cantidad>0 and estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega and recoger!=0
	--declare crsRecoger  cursor local fast_forward for select [nº orden],cantidad,recoger from linpedidovta where cantidad>0 and empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega and recoger!=0
	open crsRecoger
	if @@error != 0 begin
		raiserror('No se ha podido recoger el producto',11,1)
		rollback
		return -1
	end
	fetch next from crsRecoger into @NumOrdenRecoger,@cantidadTotal,@CantidadRecoger
		-- nos vamos recorriendo las lineas y vamos haciendo unas cosas u otras
		-- si la cantidad a entregar es cero, no insertaremos una entrega, pero si la cantidad es distinta de cero, 
		-- si q lo haremos
	while @@fetch_status=0 begin
		set @Traspaso=(select TraspasoAlmacén from contadoresglobales)
		if @@error != 0 begin
			raiserror('No se ha podido encontrar el numero de traspaso',11,1)
			rollback
			return -1
		end
		set @Traspaso = @traspaso + 1
		-- insertamos el el preextracto del producto
		if @HayLeasing=0 begin
			insert into PreExtrProducto (empresa,diario,número,fecha,[nº cliente],contactocliente,Albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,vendedor,NºPedido,NºTraspaso, Usuario)
				 select @Empresa,'_RecogFac',producto,@fecha,[nº cliente],@contacto,@ultnumalbarán + 1,'Recuperación productos albarán ' + cast((@ultnumalbarán + 1) as char),almacén,grupo,cantidad,[base imponible],delegación,[forma venta],1,[nº orden],@Vendedor,Número,@traspaso, @Usuario
				 from LinPedidoVta 
				 where [nº orden]=@NumOrdenRecoger
	
			if @@error != 0 begin
				raiserror('No se ha podido insertar en el extracto la cantidad a recoger',11,1)
				rollback
				return -1
			end
		end else if @hayleasing=1 begin -- si hay leasing lo ponemos en el cliente del leasing
			insert into PreExtrProducto (empresa,diario,número,fecha,[nº cliente],contactocliente,Albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,vendedor,NºPedido,NºTraspaso, Usuario)
				 select @Empresa,'_RecogFac',producto,@fecha,@ClienteLeasing,@contactoLeasing,@ultnumalbarán + 1,'Recuperación productos albarán ' + cast((@ultnumalbarán + 1) as char),almacén,grupo,cantidad,[base imponible],delegación,[forma venta],1,[nº orden],@Vendedor,Número,@traspaso, @Usuario 
				 from LinPedidoVta where [nº orden]=@NumOrdenRecoger
	
			if @@error != 0 begin
				raiserror('No se ha podido insertar en el extracto la cantidad a recoger',11,1)
				rollback
				return -1
			end
		end
		-- llamamos al extracto de producto
		exec @DevuelveRecoger = prdExtrProducto @empresa,'_RecogFac', @Usuario
		if @Devuelverecoger < 0 begin
			raiserror('No se han podido crear las líneas del extracto del producto',11,1)
			rollback
			return -1
		end
		-- ponemos el nuevo numero de traspaso
		update contadoresglobales set TraspasoAlmacén=@traspaso
		if @@error != 0 begin
			raiserror('No se ha podido insertar en el extracto la cantidad a recoger',11,1)
			rollback
			return -1
		end
		-- actualizamos las lineas de venta
		update linpedidovta set yafacturado=1 where [nº orden]=@NumOrdenRecoger
		if @@error != 0 begin
			raiserror('No se ha podido insertar en el extracto la cantidad a recoger',11,1)
			rollback
			return -1
		end
		set @CantidadEntregar=@cantidadTotal-@cantidadRecoger
		
		-- dependiendo de como sea la cantidad entregada tendremos que hacer unas cosas u otras
		-- si cero no hacemos nada, pero si es mayor q cero si
		if @cantidadentregar>0 begin
			-- insertamos el el preextracto del producto
			if @HayLeasing=0 begin
				insert into PreExtrProducto (empresa,diario,número,fecha,[nº cliente],contactocliente,Albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,vendedor,NºPedido,NºTraspaso, Usuario)
					 select @Empresa,'_EntregFac',producto,@fecha,[nº cliente],@contacto,@ultnumalbarán + 1,'Entrega de productos ya facturados',almacén,grupo,-@cantidadEntregar,[base imponible],delegación,[forma venta],1,[nº orden],@Vendedor,Número,@traspaso, @Usuario 
					 from LinPedidoVta where [nº orden]=@NumOrdenRecoger
	
				if @@error != 0 begin
					raiserror('No se ha podido insertar en el extracto la cantidad a entregar',11,1)
					rollback
					return -1
				end
			end else if @HayLeasing=1 begin
				insert into PreExtrProducto (empresa,diario,número,fecha,[nº cliente],contactocliente,Albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,vendedor,NºPedido,NºTraspaso, Usuario)
					 select @Empresa,'_EntregFac',producto,@fecha,@ClienteLeasing,@contactoLeasing,@ultnumalbarán + 1,'Entrega de productos ya facturados',almacén,grupo,-@cantidadEntregar,[base imponible],delegación,[forma venta],1,[nº orden],@Vendedor,Número,@traspaso, @Usuario
					 from LinPedidoVta where [nº orden]=@NumOrdenRecoger
	
				if @@error != 0 begin
					raiserror('No se ha podido insertar en el extracto la cantidad a entregar',11,1)
					rollback
					return -1
				end
			end
			exec @DevuelveRecoger = prdExtrProducto @empresa,'_EntregFac', @Usuario
			if @Devuelverecoger < 0 begin
				raiserror('No se han podido crear las líneas del extracto del producto',11,1)
				rollback
				return -1
			end			
		end -- cierra al @cantidadentregar>0	
		
	   fetch next from crsRecoger into @NumOrdenRecoger,@cantidadTotal,@CantidadRecoger
	end -- cierra al incio del bucle
	close crsRecoger
	deallocate crsRecoger
	
end -- cierra a la comprobacion de si hay alguna linea para recoger

---------------------
-- Carlos 11/11/21 -->>> ¡¡¡hasta este punto he refactorizado!!! No sigo porque ya hay un update en linpedidovta un poco más arriba
---------------------

--select @Empresa,'SYS',producto,@fecha,[nº cliente],@contacto,@ultnumalbarán + 1,texto,almacén,grupo,-cantidad,[base imponible],delegación,[forma venta],1,[nº orden],@Vendedor,Número from LinPedidoVta where empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega
insert into PreExtrProducto (empresa,diario,número,fecha,[nº cliente],contactocliente,albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,vendedor,NºPedido, Usuario) 
	select @Empresa,'SYS',producto,@fecha,[nº cliente],@contacto,@ultnumalbarán + 1,texto,almacén,grupo,-cantidad,[base imponible],delegación,[forma venta],1,[nº orden],isnull(v.Vendedor,@Vendedor),Número, @Usuario 
	from LinPedidoVta l left join VendedorLinPedidoVta v
	on l.[Nº Orden] = v.Id
	where l.empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega and (Picking > 0 or @ningunaLineaTienePicking = 1)
if @@error != 0 begin
	raiserror('No se ha podido crear el pre-extracto del producto',11,1)
	rollback
	return -1
end
declare @Devuelve  as int -- para ver qué me devuelve el prdExtrProducto
exec @Devuelve = prdExtrProducto @empresa,"SYS", @Usuario
if @Devuelve < 0 begin
	raiserror('No se han podido crear las líneas del extracto del producto',11,1)
	rollback
	return -1
end
-- Ahora buscaremos si hay algun recoger con linea de abono. Si lo hay haremos lo mismo
if (select count(numero) from LinPedidoVta where empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega and recoger!=0 and cantidad<0)>0 begin
	
set  @Traspaso =0
set  @NumOrdenRecoger=0
set  @CantidadTotal =0
set  @CantidadRecoger=0
set  @CantidadEntregar =0
	
	
	
	-- si hay alguna tendremos que hacerlo con un cursor y en base a los numeros de orden
	-- buscamos los que recoger sean mayor a cero
	declare crsRecoger  cursor local fast_forward for select [nº orden],cantidad,recoger from linpedidovta where cantidad<0 and empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and [fecha entrega]<=@fechaentrega and recoger!=0
	open crsRecoger
	if @@error != 0 begin
		raiserror('No se ha podido recoger el producto',11,1)
		rollback
		return -1
	end
	fetch next from crsRecoger into @NumOrdenRecoger,@cantidadTotal,@CantidadRecoger
		-- nos vamos recorriendo las lineas y vamos haciendo unas cosas u otras
		-- si la cantidad a entregar es cero, no insertaremos una entrega, pero si la cantidad es distinta de cero, 
		-- si q lo haremos
	while @@fetch_status=0 begin
		set @Traspaso=(select TraspasoAlmacén from contadoresglobales)
		if @@error != 0 begin
			raiserror('No se ha podido encontrar el numero de traspaso',11,1)
			rollback
			return -1
		end
		set @Traspaso = @traspaso + 1
		set @CantidadEntregar=@cantidadTotal-@cantidadRecoger
		
		-- dependiendo de como sea la cantidad entregada tendremos que hacer unas cosas u otras
		-- si cero no hacemos nada, pero si es mayor q cero si
		if @cantidadentregar!=0 begin
			-- insertamos el el preextracto del producto
			insert into PreExtrProducto (empresa,diario,número,fecha,[nº cliente],contactocliente,Albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,vendedor,NºPedido,NºTraspaso, Usuario)
				 select @Empresa,'_EntregFac',producto,@fecha,[nº cliente],@contacto,@ultnumalbarán + 1,'Entrega de productos ya facturados',almacén,grupo,-@cantidadEntregar,[base imponible],delegación,[forma venta],1,[nº orden],@Vendedor,Número,@traspaso, @Usuario
				 from LinPedidoVta where [nº orden]=@NumOrdenRecoger
	
			if @@error != 0 begin
				raiserror('No se ha podido insertar en el extracto la cantidad a entregar',11,1)
				rollback
				return -1
			end
			exec @DevuelveRecoger = prdExtrProducto @empresa,'_EntregFac', @Usuario
			if @Devuelverecoger < 0 begin
				raiserror('No se han podido crear las líneas del extracto del producto',11,1)
				rollback
				return -1
			end			
		end -- cierra al @cantidadentregar>0	
		-- insertamos el el preextracto del producto
		insert into PreExtrProducto (empresa,diario,número,fecha,[nº cliente],contactocliente,Albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,vendedor,NºPedido,NºTraspaso, Usuario)
			 select @Empresa,'_RecogFac',producto,@fecha,[nº cliente],@contacto,@ultnumalbarán + 1,'Recuperación productos albarán ' + cast((@ultnumalbarán + 1) as char),almacén,grupo,cantidad,[base imponible],delegación,[forma venta],1,[nº orden],@Vendedor,Número,@traspaso, @Usuario 
			 from LinPedidoVta where [nº orden]=@NumOrdenRecoger
		
		if @@error != 0 begin
			raiserror('No se ha podido insertar en el extracto la cantidad a recoger',11,1)
			rollback
			return -1
		end
		-- llamamos al extracto de producto
		set @DevuelveRecoger=0
		exec @DevuelveRecoger = prdExtrProducto @empresa,'_RecogFac', @Usuario
		if @Devuelverecoger < 0 begin
			raiserror('No se han podido crear las líneas del extracto del producto',11,1)
			rollback
			return -1
		end
		-- ponemos el nuevo numero de traspaso
		update contadoresglobales set TraspasoAlmacén=@traspaso
		if @@error != 0 begin
			raiserror('No se ha podido insertar en el extracto la cantidad a recoger',11,1)
			rollback
			return -1
		end
		-- actualizamos las lineas de venta
		update linpedidovta set yafacturado=1 where [nº orden]=@NumOrdenRecoger
		if @@error != 0 begin
			raiserror('No se ha podido insertar en el extracto la cantidad a recoger',11,1)
			rollback
			return -1
		end
		
		
	   fetch next from crsRecoger into @NumOrdenRecoger,@cantidadTotal,@CantidadRecoger
	end -- cierra al incio del bucle
	close crsRecoger
	deallocate crsRecoger
	
end -- cierra a la comprobacion de si hay alguna linea para recoger
/*
commit 
return -2
*/
update LinPedidoVta set [nº albarán] = @UltNumAlbarán + 1,[fecha albarán] = @fecha,estado = 2 where empresa=@empresa and número = @pedido and estado = 1 and [fecha entrega]<=@FechaEntrega and (Picking > 0 or @ningunaLineaTienePicking = 1)
if @@error != 0 begin
	raiserror('No se han podido actualizar las líneas del pedido',11,1)
	rollback
	return -1
end
-- David Sanchez Lopez.. 14/06/04
-- borramos por si hay algo en ubicaciones reservadas
delete ubicacionesreservadas from ubicacionesreservadas as u inner join linpedidovta as l
	on u.empresa=l.empresa and u.numero=l.numero and u.nºordenvta=l.[nº orden]
	where l.empresa=@empresa and l.numero=@pedido and l.[nº albaran]= @UltNumAlbarán + 1
if @@error != 0 begin
	raiserror('No se ha podido borrar de ubicaciones reservadas',11,1)
	rollback
	return -1
end
-- David Sanchez Lopez.. 05/07/04
-- Ponemos el numero de empresaAlbaranVta en la tabla ubicaciones.
-- No lo podemos poner antes porque como esta relacionado, no se puede poner hasta que no se crea el albaran
update ubicaciones set empresaAlbaranVta=@empresa where AlbaránVta=@UltNumAlbarán + 1
if @@error != 0 begin
	raiserror('No se ha podido actualizar la empresa del albarán en la tabla ubicaciones',11,1)
	rollback
	return -1
end


/*
-- Desactivado por Carlos 07/07/17: por correo de Alfredo y Manuel, dejamos de marcar el servir junto
-- Una vez que hacemos el albaran tenemos que ver si se han quedado lineas pendientes. Si se ha quedado alguna, pondremos el campo servir junto a 1.
if (select count(*) from linpedidovta where empresa=@empresa and número=@pedido and estado=-1)>=1 begin
	update cabpedidovta set servirjunto=1 where empresa=@empresa and número=@pedido
	if @@error != 0 begin
		raiserror('No se ha podido actualizar la empresa del albarán en la tabla ubicaciones',11,1)
		rollback
		return -1
	end
end
*/

-- Carlos 05/02/10
-- Comprobamos que todas las ubicaciones estén cuadradas
declare @Retorno as char(15)
exec @retorno = prdQuedaDescuadradaUbicación @Empresa,@Pedido,1

if @retorno <0 begin
		rollback
		return -1
end

-- Aviso devolución de producto

declare @Destinatarios as char(162)
declare @CuerpoCorreo as char(1000)
declare @ComentariosPedido as char(1000)
declare @NombreCliente as char(50)
declare @DirecciónCliente as CHAR(50)
DECLARE @tableHTML  NVARCHAR(MAX) ;

-- NestoAPI#395 (21/08/26): el correo saltaba con CUALQUIER linea negativa, asi que lo
-- disparaban los descuentos (TiCKET "Suscribete y ahorra", ficticio) y los pseudo-productos
-- de cuenta contable (624..., que no estan en Productos). Una devolucion de verdad es una
-- linea de PRODUCTO REAL: existe en Productos y no es ficticio. Mismo criterio que ya usa
-- este SP mas arriba al descartar ficticios en el calculo de stock negativo.
-- OJO: este if no solo manda el correo, tambien EXIGE comentario en el pedido (rollback si
-- no lo hay), asi que la condicion mala tambien impedia albaranar pedidos legitimos.
if exists (select * from linpedidovta as ldev
	inner join productos as pdev on pdev.empresa = ldev.empresa and pdev.número = ldev.producto
	where ldev.empresa = @empresa and ldev.[Nº Albarán]=@UltNumAlbaran+1 and ldev.cantidad < 0
	and ldev.tipolinea = 1 and pdev.ficticio = 0
	and ldev.producto not like 'B%' and ldev.producto not like '602%' and ldev.[Nº Cliente]<>'15191') begin
	SELECT @Destinatarios = STRING_AGG(rtrim(dbo.correovendedor(ISNULL(vendedores.Vendedor, @Vendedor))), ';')
	FROM (
		SELECT DISTINCT ISNULL(v.Vendedor, @Vendedor) AS Vendedor
		FROM LinPedidoVta l
		LEFT JOIN VendedorLinPedidoVta v ON l.[Nº Orden] = v.Id
		WHERE l.empresa = @empresa 
		  AND [Nº Albarán] = @UltNumAlbaran + 1 
		  AND cantidad < 0
	) AS vendedores;


	if @Vendedor in (select Vendedor from EquiposVenta where Superior = 'ASH') begin
		set @Destinatarios = rtrim(@Destinatarios)+'; albertosancho@nuevavision.es'
	end 

	if @NumCliente = '31517' or @NumCliente = '32624' begin
		set @Destinatarios = rtrim(@Destinatarios)+'; tiendaonline@nuevavision.es'
	end

	select @CuerpoCorreo = 'El cliente '+@NumCliente+' ha realizado una devolución de producto.'
	select @ComentariosPedido = (
		select top 1 CAST(comentarios AS CHAR(1000)) from cabpedidovta AS c inner join linpedidovta AS l
		on c.empresa=l.empresa and c.número = l.número 
		where l.empresa = @empresa and l.[Nº Albaran] = @UltNumAlbaran + 1 
		)
	if @ComentariosPedido is null or ltrim(rtrim(@ComentariosPedido))='' begin
			rollback
			raiserror('En las devoluciones de producto es obligatorio poner un comentario con el motivo de la devolución',11,1)
			return -1
	end
	select @nombrecliente = nombre, @direccióncliente = dirección from clientes where empresa = @Empresa and [nº cliente] = @NumCliente and contacto = @Contacto
	
	
	SET @tableHTML =
    N'<H1>Informe de Devolución de Producto</H1>' +
    N'<H2>'+@CuerpoCorreo+'</H2>' +
    N'<p>'+@ComentariosPedido+'</p>' +
    N'<p></p>' +
    N'<p><b>'+@NombreCliente+'</b></p>' +
    N'<p>'+@DirecciónCliente+'</p>' +   
    N'<table border="1">' +
    N'<tr><th>Producto</th><th>Texto</th>' +
    N'<th>Base Imponible</th><th>Almacén</th><th>Forma de Venta</th></tr>' +
    CAST ( ( SELECT td = producto,       '',
					td = texto, '',
					td = [base imponible], '',
					td = Almacén, '',
					td = [forma venta], ''
 					from linpedidovta where empresa = @empresa and [Nº Albarán] = @UltNumAlbarán + 1
				
              
			FOR XML PATH('tr'), TYPE 
	) AS NVARCHAR(MAX) ) +
	N'</table>' ;
	

	DECLARE @mailDevolucionId INT;
	
	EXEC @mailDevolucionId = msdb.dbo.sp_send_dbmail
    @profile_name = 'Nesto',
    @recipients = @Destinatarios,
    @copy_recipients = 'carlosadrian@nuevavision.es; manuelrodriguez@nuevavision.es',
	@body = @tableHTML,
	@body_format = 'HTML',
    @subject = 'Aviso de Devolución de Producto' ;


end



commit transaction

	-- Carlos 17/09/14: si se hacer albarán de algún pedido de venta online, informamos para marcarlo como enviado
	if (select top 1 [nº orden] from linpedidovta as l where [Nº Albarán]=@UltNumAlbarán + 1 and [Forma Venta] in ('QRU','WEB','STK','BLT')) is not null begin
				SET @tableHTML =
	    N'<H1>Envío Venta Online</H1>' +
	    N'<table border="1">' +
	    N'<tr><th>Pedido</th></tr>' +
	    CAST ( ( SELECT td = l.Número,       ''
				from linpedidovta as l 
				where [Nº Albarán]=@UltNumAlbarán + 1 and [Forma Venta] in ('QRU','WEB','STK','BLT')
	            group by l.Número
				FOR XML PATH('tr'), TYPE 
		) AS NVARCHAR(MAX) ) +
		N'</table>' ;
	
		
		DECLARE @mailOnlineId INT;
		EXEC @mailOnlineId = msdb.dbo.sp_send_dbmail
	    @profile_name = 'Nesto',
	    @recipients = 'tiendaonline@nuevavision.es',
		--@copy_recipients = 'alfredo@nuevavision.es',
	    @body = @tableHTML,
	    @body_format = 'HTML',
		@subject = 'Envío Venta Online' ;
	end



return @UltNumAlbarán + 1
-- FIN TRANSACCIÓN
GO
