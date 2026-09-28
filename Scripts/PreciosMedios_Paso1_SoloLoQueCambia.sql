-- Precios medios, PASO 1 (28/09/26). Ejecutar como sa en NV, fuera de horario o en cualquier momento (solo cambia
-- el procedimiento; no toca datos). Efecto a partir del próximo domingo.
--
-- Diagnóstico (domingo 27/09): el trabajo «Precios Medios» tarda ~5 h (07:10-12:10) y bloquea LinPedidoVta: timeouts
-- en comisiones, clientes y Verifactu. De la empresa 1: 2 h 09 min recalculando la media de 27.695 productos desde su
-- primera compra, y 2 h 39 min reescribiendo el coste de ~2,68 millones de líneas de venta (empresa 1 + espejo 3),
-- casi todas con el MISMO valor, disparando además trgLinPedidoVtaUpd en cada una.
--
-- Qué cambia (el resultado final es EL MISMO):
--   1. Los UPDATE de LinPedidoVta/LinPedidoCmp solo tocan las filas cuyo coste CAMBIA
--      («and exists (select coste except select nuevo)», que también trata bien los NULL). Una fila que ya tiene ese
--      coste no se reescribe: mismo valor final, sin bloqueo ni trigger para ella. trgLinPedidoVtaUpd no hace nada
--      cuando solo cambia el coste (reacciona a descuentos, estado, cantidad, picking...), así que no se pierde nada.
--   2. Índice agrupado en ##global (producto, fechadesde) antes de los dos joins masivos contra LinPedidoVta.
-- Nada más: ni la lógica del cálculo, ni el orden, ni los logs.
--
-- Vuelta atrás: PreciosMedios_VueltaAtras_Original.sql (el procedimiento tal cual estaba el 28/09/26).
USE NV;
GO
SET QUOTED_IDENTIFIER OFF;
GO
SET ANSI_NULLS ON;
GO
ALTER PROCEDURE [dbo].[prdActualizarPreciosMediosDeTodosLosProductos] AS
-- David Sanchez Lopez... 18/06/03...
-- Procedimiento que me actualiza los precios medios de todos los productos,
-- y el campo coste de todas las lineas de venta.
declare @empresa as char (3)
declare @EmpresaEspejo as char (3)
declare @fecha as datetime
set @fecha=getdate()
declare @Bucle as int
set @bucle=1

while @bucle<=5 begin -- lo hacemos en un bucle, y lo ponemos solo para las empresas 1 y 4.
if @bucle=1 or @bucle=4 or @bucle=5 begin
--if @bucle=1 begin
		set @empresa=cast(@bucle as char(3))
		set @Empresaespejo=(select [iva por defecto] from empresas where numero=@empresa)
	set ansi_warnings off
if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#prdPrecioMedioTemp') ) drop table [dbo].[#prdPrecioMedioTemp]

	insert into logmodificaciones (empresa,Procedimiento,Numero,fecha) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','Producto inicio',@fecha)

	--despues las creamos
	CREATE TABLE [#prdPrecioMedioTemp]
	([Empresa] [char] (3) not null,[Producto] [char] (15) not null,Factura [char] (15), FechaAlbaran [datetime],Precio [money],PrecioAntiguo [money],[kit] [bit] not null default (0),Accion [integer] not null default(0) )
             ON [PRIMARY]

-- david sanchez, me creo una tabla global para ir insertando luego en actualizar precio medio producto
if exists (select * from tempdb..sysobjects where id = object_id('tempdb..##Global') ) drop table [dbo].[##Global]
CREATE TABLE [##global]
	([NºOrden] [int] IDENTITY (100, 100) NOT NULL,[borrar] [bit] not null default (0) ,[Producto] [char] (15) not null,[FechaDesde] [datetime] ,[FechaHasta] [datetime] ,[PrecioMedio] [money]  NOT NULL  default(0))
             ON [PRIMARY]

	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end
	-- insertamos los productos que tengan compras en la tabla temporal.
	-- David Sanchez Lopez 26/06/03.. La select la cambio por subselect para que coja bien las facturas
	--antes buscabamos el min(factura) y entonces no me correspondia la factura con la fecha del albaran.
	insert into #prdPrecioMedioTemp (empresa,producto,fechaalbaran,factura)
		select @empresa,l.producto,min(l.fechaalbaran),l.nºfactura from linpedidocmp as l
		where (l.empresa=@empresa or l.empresa=@empresaespejo) and l.tipolinea=1
		and nºfactura=(select min(cast(nºfactura as int)) from linpedidocmp as l2  where (empresa=@empresa or empresa=@empresaespejo) and tipolinea=1 and producto=l.producto and fechaalbaran=
			(select min(fechaalbaran) from linpedidocmp where (empresa=@empresa or empresa=@empresaespejo) and tipolinea=1 and producto=l.producto))
			and l.producto is not null
			group by l.producto,l.nºfactura
			order by l.producto

	-- David Sanchez 07/07/003
	-- Borramos los ficticios
	delete #prdPrecioMedioTemp from  #prdPrecioMedioTemp as t inner join productos as p on
		t.empresa=p.empresa and t.producto=p.número
		where p.ficticio=1 --or p.familia <> 'Anubis'
		--*********************************************************************************************************************

-- David Sánchez 30/05/07 borramos los que el ultimo movimiento sea montar o desmontar un kit
-- ya que estos los consideramos como que no tienen compras
if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#Montajes') ) drop table [dbo].[#Montajes]
	--despues las creamos
	CREATE TABLE [#Montajes]
	(Producto [char] (15),
	 Montaje [datetime],
	 Albaran [datetime] not null default('01/01/1990')
	)
             ON [PRIMARY]

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#PuenteMontajes') ) drop table [dbo].[#PuenteMontajes]
	--despues las creamos
	CREATE TABLE [#PuenteMontajes]
	(Producto [char] (15),
	 Albaran [datetime]
	)
             ON [PRIMARY]

/*
-- Carlos 30/03/17: sustituyo estos dos insert por los dos de abajo
insert into #montajes (producto,montaje)
	select numero,max(fecha) from extractoproducto where (empresa=@empresa or empresa = @EmpresaEspejo) and diario='_MontarKit' and cantidad>0 group by numero

insert into #Puentemontajes (producto,albaran)
	select numero,max(fecha) from extractoproducto where (empresa=@empresa or empresa = @EmpresaEspejo) and nºproveedor is not null and cantidad>0 group by numero
*/
insert into #montajes (producto,montaje)
	select numero,max(fecha) from extractoproducto where (empresa=@empresa or empresa = @EmpresaEspejo) and diario='_MontarKit' and texto like 'Montaje %' and cantidad>0 and número in (select distinct(Producto) from #prdPrecioMedioTemp) group by numero

insert into #Puentemontajes (producto,albaran)
	select numero,max(fecha) from extractoproducto where (empresa=@empresa or empresa = @EmpresaEspejo) and nºproveedor is not null and cantidad>0 and número in (select distinct(Producto) from #prdPrecioMedioTemp) group by numero


update #montajes set albaran=p.albaran from #puentemontajes as p inner join #montajes as m on p.producto=m.producto

delete #prdPrecioMedioTemp where producto in(
select producto from #montajes where montaje>albaran
)



-- David Sanchez 23/03/04
-- tenemos que meter los kits
-- David Sánchez.... 30/05/07 .. Los kits no los metemos
-- ya que los que no tengan compra se trataran como un producto normal.
/*
insert into #prdpreciomediotemp( empresa,producto,kit)
select @empresa,k.número,1 from (
	select empresa,número,númeroasociado,cantidad from kits  where número in(
	select número from kits where empresa=@empresa and númeroasociado in (select producto from #prdpreciomediotemp))) as k
		group by número
-- Borramos los kit que ya estaban metidos porque tenian compra
delete #prdpreciomediotemp where kit=1 and producto in (select producto from #prdpreciomediotemp where kit=0)
-- nos recorremos los kit que haya
-- y vamos actualizando las fechas
declare @ProductoKit as char(15)
declare @FacturaCur as char(15)
declare @FechaAlbaranCur as datetime
	declare crsKit cursor local dynamic for select producto from #prdpreciomediotemp where kit=1
	open crskit
	fetch next from crsKit into @ProductoKit
		while @@fetch_status=0 begin -- Venta grupo es el 80 por ciento. a medida que vamos pasando vamos borrando.
			-- buscamos las fechas
			set @FechaAlbaranCur=(select min(fechaAlbaran) from #prdpreciomediotemp where producto in (select númeroasociado from kits where número=@productokit))
			set @facturacur=(select min(factura) from #prdpreciomediotemp where producto in (select númeroasociado from kits where número=@productokit))
			-- actualizamos
			update #prdpreciomediotemp set factura=@facturacur,fechaalbaran=@fechaalbarancur where current of crsKit
			fetch next from crsKit into @ProductoKit
		end
	close crsKit
	deallocate crsKit
*/
	update #prdPrecioMedioTemp set precioantiguo=(select preciomedio from productos where empresa=@empresa and número=#prdPrecioMedioTemp.producto)

	declare @Producto as char(15)
	declare @Factura as int
	declare @FechaAlbaran as datetime
	declare @DevueltoPrecioMedioProducto as smallint
	declare @ValorAntiguo as money
	declare crsActualizar cursor fast_forward for select Producto,Factura,FechaAlbaran,precioantiguo from #prdPrecioMedioTemp where factura is not null order by producto asc
	open crsActualizar
	fetch next from crsActualizar into @Producto,@Factura,@FechaAlbaran,@ValorAntiguo
	while @@fetch_status = 0 begin
		-- tendremos que llamar a actualizar los precios medios de todos los productos que haya en la tabla
--		exec @DevueltoPrecioMedioProducto=prdActualizarPrecioMedioProducto @empresa,@empresaEspejo, @Factura,1,@Producto ,@FechaAlbaran
		print @producto

		exec @DevueltoPrecioMedioProducto=prdLLamarActualizarPrecioMedioProducto @Empresa,@Producto,@FechaAlbaran
		-- depende lo q devuelva precio medio insertaremos una cosa u otra en el log

		if @DevueltoPrecioMedioProducto<>1 begin -- es que ha salido con error del otro procedimiento con error
			insert into logmodificaciones (empresa,Procedimiento,Numero,fecha,valorantiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','No se puedo actual. '+@producto,@fecha,cast(@ValorAntiguo as char))
		end else begin
  			insert into logmodificaciones (empresa,Procedimiento,Numero,fecha,valorantiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','Actualizado producto '+@producto,@fecha,cast(@ValorAntiguo as char))
		end
		fetch next from crsActualizar into @Producto,@Factura,@FechaAlbaran,@ValorAntiguo

	end

	close crsActualizar
	deallocate crsActualizar

	-- 28/09/26 (Carlos): índice en ##global para los joins contra LinPedidoVta (antes, sin ninguno)
	create clustered index IX_global_producto on ##global (producto, fechadesde)

	-- David Sanchez Lopez 10/11/03.....
	-- Lo pongo con la tabla temporal ya que cambie el prdactualizar precio medio producto
	update linpedidovta set coste=t.preciomedio from linpedidovta as l inner join ##global as t
		on l.producto=t.producto and
			(l.estado>=2 and ( l.[fecha albaran] >=t.fechadesde and l.[fecha albaran] <t.fechahasta ))
			where (l.empresa=@empresa or l.empresa=@empresaespejo)
			and exists (select l.coste except select t.preciomedio) -- 28/09/26: solo lo que cambia

	if @@error!=0 begin
		raiserror ('No se pueden actualiza las lineas de ventas',16,1)
		return -1
	end

	-- a continuacion tengo que actualizar las de estado 1 y -1
	-- para ello borro las que esten repetidas de la tabla temporal, ya que en realidad solo quiero
	-- el ultimo
	declare @Producto2 as char (15)
	declare @NumOrden as int
	declare @ProductoAnterior as char(15)

	declare crsBorrar cursor  for select producto,[nºorden] from ##global order by producto,fechahasta desc  -- ordenado por fecha para quedarnos con la ultima
	if @@error!=0 begin
		raiserror ('No se pueden actualiza las lineas de ventas',16,1)
		return -1
	end
	open  crsBorrar
	fetch next from crsBorrar into @Producto2,@NumOrden
	while @@fetch_status = 0 begin
		if @producto2=@productoanterior begin
			update  ##global set borrar=1 where [nºorden]=@numorden
			if @@error!=0 begin
				raiserror ('No se pueden actualiza las lineas de ventas',16,1)
				return -1
			end
		end
		set @productoanterior=@producto2
		fetch next from crsBorrar into @Producto2,@NumOrden


	end

	close crsBorrar
	deallocate crsBorrar

	-- borramos los que el campo borrar sea uno
	delete ##global where borrar=1
	if @@error!=0 begin
		raiserror ('No se pueden actualiza las lineas de ventas',16,1)
		return -1
	end
	-- actualizamos la tabla
	update linpedidovta set coste=t.preciomedio from linpedidovta as l inner join ##global as t
		on l.producto=t.producto and
			(l.estado=-1 or l.estado=1)
		where (l.empresa=@empresa or l.empresa=@empresaespejo)
		and exists (select l.coste except select t.preciomedio) -- 28/09/26: solo lo que cambia
	if @@error!=0 begin
		raiserror ('No se pueden actualiza las lineas de ventas',16,1)
		return -1
	end
	-- insertamos los productos q no esten en la tabla (que seran los que no hayan hecho compras
	/* esto hay que dejarlo!!!!!
	insert into #prdPrecioMedioTemp (empresa,producto,precioantiguo) select @empresa,número,preciomedio from productos
		where empresa=@empresa and ficticio=0 and número not in (select producto from #prdPrecioMedioTemp)	*/
		--*********************************************************************************************************************
		--*********************************************************************************************************************
		--*********************************************************************************************************************
		--*********************************************************************************************************************
		--*********************************************************************************************************************
	--Borramos los que queden no nulos para actualizar los nulos
	delete #prdPrecioMedioTemp where factura is not null

-- David Sánchez 30/05/07 Actualizamos el campo accion
	-- La 1 sera calcular mediante descuento
	-- La 2 sera kit que se hace mediante el sumatorio de costes
	-- La 3 sera mediante el descuento pero para productos, luego lo cambiamos por la 1
	-- La 4 sera imputando costes
	-- La 5 sera para todos los productos que siendo 4, no tienen kits con compras, es decir, todos los que sean productos asociados de algun kit que este en la temporal
			--Se haran por descuento asique los pondremos a 1
	update #prdPrecioMedioTemp set accion=1 where producto in (select numero from kits where empresa=@empresa) -- ponemos a 1 todos los kits
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end
	update #prdPrecioMedioTemp set accion=2 where accion=1 and producto  in (select numero from kits where empresa=@empresa and   numeroasociado not in(select producto from #prdPrecioMedioTemp)) -- ponemos a 2 todos los kits que tengan algun producto asociado con compras
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end

	update #prdPrecioMedioTemp set accion=3 where accion=0 -- Ponemos a 3 todos los productos que queden, q seran los que no son kits (luego lo cambiaremos por la 1)
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end
	update #prdPrecioMedioTemp set accion=4 where accion=3 and producto  in (select numeroasociado from kits where empresa=@empresa) -- Ponemos a 4 todos los 3 que seran los productos que forman parte de algun kit
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end
	update #prdPrecioMedioTemp set accion=5 where accion=4 and producto  in (select numeroasociado from kits where empresa=@empresa and numero in(select producto from #prdPrecioMedioTemp)) -- Ponemos a 5 todos los 4 (asociados) que tengan kits sin compras
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end
	update #prdPrecioMedioTemp set accion=1 where accion=3
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end
	update #prdPrecioMedioTemp set accion=1 where accion=5
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla global',16,1)
		return -1
	end

	-- con un cursor producto a producto vamos buscando el precio
	declare @ProductoNulo as char(15)
	declare @ValorAnt as money
	declare @Accion  as integer
	declare @precio as money

	declare crsActualizarPrecio cursor fast_forward for select Producto,PrecioAntiguo,Accion from #prdPrecioMedioTemp order by accion asc
	open crsActualizarPrecio
	fetch next from crsActualizarPrecio into @ProductoNulo,@ValorAnt,@Accion
	while @@fetch_status = 0 begin
	if @Accion=4 begin -- Son los productos que forman parte de algun kit con compras.
		declare @Kit as char(15)
		declare @ImporteTotalKit as money
		declare @PrecioMedioKit as money
		declare @ImporteDelProducto as money
		declare @CantidadProducto as int
		-- Buscar el kit
		set @kit=(select top 1 numero from kits where empresa=@empresa and numeroasociado=@productoNulo)

		-- ImporteTotalKit
		set @ImporteTotalKit=(select sum(p.pvp*k.cantidad) from kits as k inner join productos as p on k.empresa=p.empresa and k.numeroasociado=p.numero
			where k.empresa=@empresa and  k.numero=@Kit)
		-- PrecioMedioKit
		set @PrecioMedioKit=(select preciomedio from productos where numero=@Kit and empresa=@Empresa)
		-- ImporteDelProductoKit
		set @ImportedelProducto=(select sum(p.pvp*k.cantidad) from kits as k inner join productos as p on k.empresa=p.empresa and k.numeroasociado=p.numero
			where k.numero=@Kit  and k.numeroasociado=@ProductoNulo)
		set @CantidadProducto=(select sum(k.cantidad) from kits as k inner join productos as p on k.empresa=p.empresa and k.numeroasociado=p.numero
			where k.numero=@Kit  and k.numeroasociado=@ProductoNulo)

		set @Precio=((@preciomediokit/@importetotalkit)*@importeDelProducto)/@CantidadProducto

		-- Actualizamos las lineas de venta
		if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#Lineas') ) drop table [dbo].[#Lineas]
		CREATE TABLE [#Lineas]
			([Producto] [char] (15),
			 [FechaDesde] [datetime] not null,
			 [FechaHasta] [datetime] ,
			 [PrecioMedioKit] [money] not null default (0),
			 [ImporteTotalKit] [money] not null default (0),
			 [ImportedelProducto] [money] not null default (0),
		              [CantidadProducto] [int] not null default (0),
		              [Precio] [money] not null default (0))
			   ON [PRIMARY]



		insert into #lineas (producto,fechaDesde,preciomediokit,importedelproducto,cantidadproducto,importetotalkit)
			select @ProductoNulo,fechaalbaran,sum(baseimponible/cantidad),@ImporteDelProducto,@CantidadProducto,@importetotalkit
			 from linpedidocmp where producto=@kit and estado>=2 group by fechaalbaran

		update #lineas set precio=((preciomediokit/importetotalkit)*importeDelProducto)/CantidadProducto

		-- Ponemos las fechas hasta para el intervalo
		update #lineas set fechahasta=a.fechadesde from #lineas as t inner join (
			select l.fechaalbaran,t.fechadesde from linpedidocmp  as l inner join #lineas as t
				 on  l.producto=@kit
				where l.[fechaalbarán]<t.fechadesde and  l.[nºalbarán]=(select top 1  [nºalbarán] from linpedidocmp where (empresa=@empresa or empresa = @EmpresaEspejo) and [fechaalbarán]<t.fechadesde and producto=l.producto and tipolinea=1  order by fechaAlbarán desc)
			) as a on t.fechadesde=a.fechaalbaran


		update #lineas set fechahasta=2958463 where fechahasta is null

		-- Actualizamos los precios medios de las lineas de venta
		update linpedidovta set coste=t.precio  from linpedidovta as l inner join #lineas as t
				on l.producto=t.producto and
					(l.estado>=2 and ( l.[fecha albaran] >=t.fechadesde and l.[fecha albaran] <t.fechahasta ))
					where (l.empresa=@empresa or l.empresa=@empresaespejo)
					and exists (select l.coste except select t.precio) -- 28/09/26: solo lo que cambia

	end else if @Accion=2 begin -- Es un kit que tiene productos asociados con compra, el precio medio sera un sumatorio de los costes de los productos que lo componen.
		/*
		set @Precio=(select sum(k.cantidad*p.preciomedio) from kits as k inner join productos as p on k.empresa=p.empresa and k.numeroasociado=p.numero
			where k.numero=@productonulo and k.empresa=@empresa)
		*/

		-- El precio lo tenemos que sacar de la suma de los precios medios de los productos que los componen
		-- pero si el precio medio es mayor al precio de venta ponemos el precio de venta.
		set @Precio=(select sum(precio) from (
				select sum(k.cantidad*p.preciomedio) as precio
				from kits as k inner join productos as p on k.empresa=p.empresa and k.numeroasociado=p.numero
				where k.numero=@productonulo  and k.empresa=@Empresa
				and p.pvp>=p.preciomedio
				union
				select sum(k.cantidad*p.pvp)
				from kits as k inner join productos as p on k.empresa=p.empresa and k.numeroasociado=p.numero
				where k.numero=@productonulo  and k.empresa=@Empresa
				and p.pvp<p.preciomedio) as a
			)

		if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#Lineas2') ) drop table [dbo].[#Lineas2]
		CREATE TABLE [#Lineas2]
			([Producto] [char] (15),
			 [FechaDesde] [datetime] not null,
			 [FechaHasta] [datetime] ,
			 [Precio] [money] not null default (0))
			   ON [PRIMARY]

		if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#LineasPuente') ) drop table [dbo].[#LineasPuente]
		CREATE TABLE [#LineasPuente]
			([Producto] [char] (15),
			 [FechaDesde] [datetime] not null,
			 [CantidadKit] [int] not null default(0),
			 [PrecioMedio] [money] )
			   ON [PRIMARY]

		-- insertamos los intervalos para cada fecha
		insert into #lineas2 (producto,fechaDesde)
			select @ProductoNulo,fechaalbaran
				 from linpedidocmp where (empresa=@empresa or empresa=@empresaespejo) and  producto in (select numeroasociado from kits where empresa=@empresa and numero=@productonulo)
					 and estado>=2 group by fechaalbaran


		-- insertamos todos los numeros asociados con las fechas desde para calcular el precio medio que tenian
		-- en ese intervalo
		insert into #LineasPuente (Producto,fechadesde,cantidadkit)
		select k.numeroasociado,l.fechadesde,k.cantidad from kits as k inner join #lineas2 as l on k.numero=l.producto
			order by fechadesde

		-- Con un bucle nos recorremos producto a producto para ver el precio medio que
		-- tenian en ese momento
		declare @ProductoP as char(15)
		declare @FechaP as datetime
		declare @Coste as money
		declare crsPvp  cursor local dynamic for select producto,fechadesde from #LineasPuente
		open crsPvp
		fetch next from crsPvp into @ProductoP,@FechaP
		while @@fetch_status=0 begin

			set @coste=(select top 1 isnull(coste,0) from  linpedidocmp
			where (empresa=@Empresa or empresa=@Empresaespejo) and producto=@productoP and fechaalbaran<=@fechaP order by fechaalbaran desc)
			update #lineaspuente set preciomedio=@coste where current of crsPvp

		fetch next from crsPvp into @ProductoP,@FechaP
		end
		close crsPvp
		deallocate crsPvp

		-- Los que se hayan quedado a null los actualizamos con el precio de la ficha
		update #LineasPuente set preciomedio=p.preciomedio from productos as p inner join #lineasPuente as l
			on p.numero=l.producto and l.preciomedio is null
			where p.empresa=@Empresa

		-- Borramos lo que habia metido en las lineas y lo insertamos con los precios
		delete #lineas2

		insert into #lineas2 (producto,fechadesde,precio)
			select @Productonulo,fechadesde,sum(cantidadkit*preciomedio) from #LineasPuente group by fechadesde

		-- Ponemos las fechas hasta para el intervalo
		update #lineas2 set fechahasta=a.fechadesde from #lineas2 as t inner join (
		select l.fechaalbaran,t.fechadesde from linpedidocmp  as l inner join #lineas2 as t
		 on  l.producto in (select numeroasociado from kits where empresa=@empresa and numero=@productonulo)
		where l.[fechaalbarán]<t.fechadesde and  l.[nºalbarán]=(select top 1  [nºalbarán] from linpedidocmp where (empresa=@empresa or empresa=@EmpresaEspejo) and [fechaalbarán]<t.fechadesde and producto=l.producto and tipolinea=1  order by fechaAlbarán desc)
		) as a on t.fechadesde=a.fechaalbaran


		update #lineas2 set fechahasta=2958463 where fechahasta is null



		-- Actualizamos los precios medios de las lineas de venta
		update linpedidovta set coste=t.precio  from linpedidovta as l inner join #lineas2 as t
				on l.producto=t.producto and
					(l.estado>=2 and ( l.[fecha albaran] >=t.fechadesde and l.[fecha albaran] <t.fechahasta ))
					where (l.empresa=@empresa or l.empresa=@empresaespejo)
					and exists (select l.coste except select t.precio) -- 28/09/26: solo lo que cambia


	end else if @Accion=1  begin -- Lo calculamos en base a los descuentos

		declare @Descuento as decimal(5,2)
		declare @PrecioEspecial as money
		declare @precioficha as money
		declare @DescuentoGrupo as decimal(5,2)
		declare @Grupo as char(3)
		declare @PrecioGrupo as money

		-- Buscamos el grupo
		select @grupo=grupo from productos where empresa=@empresa and número=@productonulo

		-- primero buscamos el precio en la ficha
		set @precioficha=(select pvp  from productos where empresa=@empresa  and número=@ProductoNulo)
		set @precio=@precioficha
		--miramos si tiene descuentos
		set @descuento= (select min(cast(descuento as decimal (5,2))) from descuentosproducto where empresa=@empresa and [Nºproveedor] is not null   and [nº producto]=@productonulo)
		if @descuento is not null begin
			set @precio=@precio*(1-@descuento)
		end

		-- miramos si el descuento lo tiene por grupo
		set @descuentoGrupo= (select min(cast(descuento as decimal (5,2))) from descuentosproducto where empresa=@empresa and [Nºproveedor] is not null   and grupoproducto=@Grupo)
		if @descuentoGrupo is not null begin
			set @precioGrupo=@precioficha*(1-@descuentogrupo)
			if @precio=@precioficha begin -- si el precio es igual al precio q tiene la ficha es que no tiene ningun descuento, por lo q se pone este

				set @precio=@preciogrupo
			end

		end
		set @precioespecial=(select max(precio) from descuentosproducto where empresa=@empresa and [Nºproveedor] is not null  and [nº producto]=@productonulo and precio<@precioficha)
		if @precioespecial is not null begin -- si tiene precio especial se pone este independientemente d los descuentos

			set  @precio=@precioespecial

		end
	end -- cierra al @Accion
		-- hacemos el update del precio medio de los productos y de las lineas de venta.
		update productos set PrecioMedio=@precio where Número=@productonulo and empresa=@empresa
		if @@error!=0 begin
			insert into logmodificaciones (empresa,Procedimiento,Numero,fecha,valorantiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','Error al actualizar '+@productoNulo,@fecha,cast(@ValorAnt as char))
		end else begin
			insert into logmodificaciones (empresa,Procedimiento,Numero,fecha,valorantiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','Actualizado producto '+@productoNulo,@fecha,cast(@ValorAnt as char))
		end
		if @Accion=1 begin
			update linpedidovta set coste=@precio where producto=@productonulo and  (empresa=@empresa or empresa=@empresaespejo)
				and exists (select coste except select @precio) -- 28/09/26: solo lo que cambia
		end

		if @@error!=0 begin
			insert into logmodificaciones (empresa,Procedimiento,Tabla,Numero,fecha,valorAntiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','LinPedidoVta','Error al actualizar '+@productoNulo,@fecha,cast(@ValorAnt as char))
		end else begin
			insert into logmodificaciones (empresa,Procedimiento,Tabla,Numero,fecha,ValorAntiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','LinPedidoVta','Actualizado producto '+@productoNulo,@fecha,cast(@ValorAnt as char))
		end
	-- Actualizamos las lineas de compra porque es probable que tenga lineas de compra pero que no estan facturadas
		update linpedidocmp set coste=@precio where producto=@productonulo and  (empresa=@empresa or empresa=@empresaespejo)
			and exists (select coste except select @precio) -- 28/09/26: solo lo que cambia

		if @@error!=0 begin
			insert into logmodificaciones (empresa,Procedimiento,Tabla,Numero,fecha,valorAntiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','LinPedidoVta','Error al actualizar '+@productoNulo,@fecha,cast(@ValorAnt as char))
		end else begin
			insert into logmodificaciones (empresa,Procedimiento,Tabla,Numero,fecha,ValorAntiguo) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','LinPedidoVta','Actualizado producto '+@productoNulo,@fecha,cast(@ValorAnt as char))
		end
		fetch next from crsActualizarPrecio into @ProductoNulo,@ValorAnt,@Accion
	end -- cierra al bucle

	close crsActualizarPrecio
	deallocate crsActualizarPrecio
	insert into logmodificaciones (empresa,Procedimiento,Numero,fecha) values (@empresa,'prdActualizarPreciosMediosDeTodosLosProductos','Producto Final',@fecha)
end -- cierra al if de si es empresa
	set @bucle=@bucle+1
end-- cierra al primer while
GO
