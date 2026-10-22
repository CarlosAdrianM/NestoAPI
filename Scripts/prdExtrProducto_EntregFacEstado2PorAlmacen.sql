-- 22/10/26: las entregas de productos ya facturados (_EntregFac) restaban de las filas «pendiente de ubicar»
-- (Ubicaciones estado 2) de CUALQUIER almacén: el UPDATE no filtraba por almacén ni por empresa, y si había varias
-- filas en estado 2 restaba la cantidad entera en cada una.
-- Caso real: nota de entrega 928959 (Reina, 21/10/26 13:22, 1 x 41823) restó 1 de la fila 322773401 de Algete
-- (estado 2, desmontaje del kit 45383) → stock ALG 3, ubicaciones 2.
-- Cambio: solo almacenes con ControlUbicaciones=1, mismo almacén y empresa (o espejo), y se reparte por las filas en
-- estado 2 de la más antigua a la más nueva sin dejar ninguna en negativo (como el cursor de prdCrearAlbaránCmp).
-- Solo cambia ese bloque; el resto es el cuerpo vigente en producción (modificado por última vez el 10/03/2025).
--
-- Lanzar como sa. El SP está creado con ANSI_NULLS ON y QUOTED_IDENTIFIER ON: se mantienen.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER PROCEDURE [dbo].[prdExtrProducto] @Empresa char(3), @Diario char(10), @Usuario varchar(30) = NULL AS
-- Comprobar empresa
--declare @Empresa char(3)
/*
select @Empresa = Empresa from SesionesUsuario where sesión = suser_sid()
if @Empresa = null begin
	raiserror('No puede albaranear desde aquí. Necesita entrar en alguna
 empresa',1,1)
	return 1 -- sale del procedimiento con errorend
*/

-- Carlos 05/03/25: si no pasan el parámetro usuario ponemos el del sistema
if @Usuario is null OR CHARINDEX('$', SUSER_NAME()) = 0 begin
	set @Usuario = SYSTEM_USER
end

--set @empresa = 1 --hasta que solucione de lo arriba
declare @Espejo as char(3)
set @Espejo=(select [iva por defecto] from empresas where número=@empresa)
-- si el espejo es nulo tendremos que buscar que empresa tiene de iva por defecto esta
declare @ProductoUbicacion as char(10)
set @ProductoUbicacion=12375
-- David Sanchez 24/07/03.. 
-- Comprobamos las fechas permitidas
declare @FechaEsVálida as int 
declare @FechaMaxima as datetime
declare @FechaMinima as datetime
set @fechaminima=(select min(fecha) as fecha from PreExtrProducto where empresa=@Empresa and diario = @diario and estado>=0)
set @fechaMaxima=(select max(fecha) as fecha from PreExtrProducto where empresa=@Empresa and diario = @diario and estado>=0)
if @fechaminima is not null begin
	--Primero miramos la fecha minima
	exec @FechaEsVálida = prdFechaEsVálida @Empresa, @FechaMinima, 5 --Extracto producto
	if @FechaEsVálida<=0 begin
		raiserror('Fecha no permitida',16,1)
		return -1 -- sale del procedimiento con error
	end else begin -- Si vale comprobamos la fecha maxima
		exec @FechaEsVálida = prdFechaEsVálida @Empresa, @FechaMaxima, 5 -- Extracto producto
		if @FechaEsVálida<=0 begin
			raiserror('Fecha no permitida',16,1)
			return -1 -- sale del procedimiento con error
		end 
	end
end
-- David Sanchez Buscamos el diario de reposicion para el almacen por defecto 22/06/04
-- lo que buscamos es el diario de salida, ya que es el que queremos
declare @UsuarioParams as char(15)
declare @Almacen as char(3)
--set @Usuario = stuff(system_user,1,patindex('%\%',system_user),'')
set @UsuarioParams = stuff(system_user,1,patindex('%\%',@Usuario),'')
if @@error != 0 begin
	raiserror('No se ha podido determinar el usuario',11,1)
	return -1
end
set @Almacen = (select valor from ParámetrosUsuario where empresa = @Empresa and Clave = 'AlmacénPedidoVta' and usuario = @UsuarioParams)
if @@error != 0 begin
	raiserror('No se ha podido determinar el almacén',11,1)
	return -1
end
declare @DiarioRepo as char(15)
declare @DiarioEntradaRepo as char(15)
set @DiarioRepo=(select DiarioSalidaRep from almacenes where empresa=@empresa and numero=@almacen)
if @@error != 0 begin
	raiserror('No se ha podido determinar el diario de reposición',11,1)
	return -1
end
set @DiarioEntradaRepo=(select DiarioEntradaRep from almacenes where empresa=@empresa and numero=@almacen)
if @@error != 0 begin
	raiserror('No se ha podido determinar el diario de reposición',11,1)
	return -1
end
if ltrim(rtrim(@diario))='General2' begin
	set @diarioRepo=@diario
end


--- David Sánchez 26/11/08 
-- No tenemos que dejar contabilizar nada de mermas sin centro de coste
-- Si es el diario de mermas
if @diario in (select numero from diariosproducto where numero like 'merma%' and empresa=@Empresa)  begin
	if exists (select 1 from preextrproducto where diario=@diario and empresa=@Empresa  and (centrocoste is null or ltrim(rtrim(centrocoste))='')) begin
		raiserror('No puede contabilizar una merma sin centro de coste',11,1)
		return -1	
	end 
	/*
	-- Carlos 30/11/23: refactorizo arriba
	if (select count(*) from preextrproducto where diario=@diario and empresa=@Empresa  and (centrocoste is null or ltrim(rtrim(centrocoste))=''))>=1 begin
		raiserror('No puede contabilizar una merma sin centro de coste',11,1)
		return -1	
	end 
	*/
end 

-- Carlos 14/12/23: no se pueden montar kits desde Nesto viejo
if @Diario = '_MontarKit' and exists (
	select * 
	from Almacenes a inner join PreExtrProducto p
	on a.Empresa = p.Empresa and a.Número = p.Almacén and p.Diario = @Diario
	where a.ControlUbicaciones = 1
) and not exists (
	select * from Ubicaciones where Empresa = @Empresa and NºTraspaso = (select top 1 NºTraspaso from PreExtrProducto where Empresa = @Empresa and Diario = @Diario) and Estado = -102
) begin
	raiserror('Ya no se pueden montar ni desmontar kits desde Nesto viejo',11,1)
	return -1	
end

-- David Sanchez Lopez... 18/05/04
-- tenemos que comprobar que si hay alguna linea con cantidad negativa, de un almacen que tenga control ubicaciones
-- si no tiene ni pasillo ni fila ni columna, no le dejamos hacerlo ya que no sabemos de donde sale
-- Filtramos por que el linpedido sea null, ya que si no lo es es un albaran y ya lo hemos dividido
-- David Sanchez... si el diario es el de entrada o recogida y el campo linpedido es nulo, no hacemos nada de las ubicaciones
declare @LinPedido as int
declare @AplicarUbicaciones as bit
set @linpedido=0
set @linpedido=(select top 1 linpedido from preextrproducto where empresa=@empresa and diario=@diario)
if (@diario='_EntregFac' or @diario='_RecogFac') and @linpedido is null begin	
	set @AplicarUbicaciones=0
end else begin
	set @AplicarUbicaciones=1
end

if @AplicarUbicaciones=1 begin
	-- David Sanchez... si el diario es el de recuperar compras, lo que tendremos que hacer es poner la linea de 2 que correponda como -2, ya que
	-- al recuperlo no lo tiene que ubicar
	if ltrim(rtrim(@Diario))='_RecFacCmp' begin
		update ubicaciones set estado=-2  from ubicaciones as u inner join preextrproducto as p on 
			u.empresa=p.empresa and u.nºordencmp=p.linpedido
				where p.diario=@Diario and p.empresa=@Empresa  and u.estado=2
	end
	if @@error!=0 begin
		raiserror ('No se pudo determinar la ubicacion',16,1)
		return -1
	end
	
	-- David Sanchez, si el diario es disitinto al de la regularizacion ni en el de la reposicion
	-- si lo es no lo hacemos ya que ya esta hecho

	if ltrim(rtrim(@diario))<>'_RegInvent' and ltrim(rtrim(@diario))<>ltrim(rtrim(@diariorepo)) and ltrim(rtrim(@diario))<>'_MontarKit' begin
		if (
			select count(p.empresa) from preextrproducto as p inner join almacenes as a
			on p.empresa=a.empresa and p.almacen=a.número
			where (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null)) and  p.empresa=@empresa and a.controlubicaciones=1 and p.diario=@diario and p.cantidad<0
				and ((p.pasillo is null or ltrim(rtrim(p.pasillo))='') or
				(p.fila is null or ltrim(rtrim(p.fila))='') or	
				(p.columna is null or ltrim(rtrim(p.columna))='') 
				)) >0 begin
	
			declare @Ubicaciones as int
			set @Ubicaciones=0
	
			-- tenemos que ver cuantas ubicaciones hay del producto, si solo hubiera una le ponemos ese pasillo, fila y columna
			-- si hubiera mas de una y no tiene una ubicacion predefinida le ponemos el error
			if @espejo is null begin	
				if exists (
					select 1 from (
					select u.almacen,u.número,count(*) as cantidad -- este count(*) también habrá que quitarlo en el futuro, lo dejo de momento solo por precaución
					from ubicaciones as u inner join preextrproducto as p
					on u.empresa=p.empresa and u.número=p.número and u.almacen=p.almacen
					left join linpedidovta as l on p.empresa=l.empresa and l.[nº orden]=p.linpedido 
					where   (l.picking<=0 or l.picking is null) and  p.diario=@Diario and p.cantidad<0 and u.estado=0
					and ((p.pasillo is null or ltrim(rtrim(p.pasillo))='') or
					(p.fila is null or ltrim(rtrim(p.fila))='') or	
					(p.columna is null or ltrim(rtrim(p.columna))='') ) and (u.pasillo is not null  and u.fila is not null and u.columna is not null)
					group by u.almacen,u.número
					having count(*)>1) as a) begin
			
					if @@error!=0 begin
						raiserror ('No se pudo determinar la ubicacion',16,1)
						return -1
					end 				
			
					set @Ubicaciones=1
				end else begin
					set @Ubicaciones=0
				end
				/*
				-- Carlos 30/11/23 refactorizo arriba
				if (
					select isnull(count(*),0) from (
					select u.almacen,u.número,count(*) as cantidad from ubicaciones as u inner join preextrproducto as p
					on u.empresa=p.empresa and u.número=p.número and u.almacen=p.almacen
					left join linpedidovta as l on p.empresa=l.empresa and l.[nº orden]=p.linpedido 
					where   (l.picking<=0 or l.picking is null) and  p.diario=@Diario and p.cantidad<0 and u.estado=0
					and ((p.pasillo is null or ltrim(rtrim(p.pasillo))='') or
					(p.fila is null or ltrim(rtrim(p.fila))='') or	
					(p.columna is null or ltrim(rtrim(p.columna))='') ) and (u.pasillo is not null  and u.fila is not null and u.columna is not null)
					group by u.almacen,u.número
					having count(*)>1) as a)>=1 begin
			
					if @@error!=0 begin
						raiserror ('No se pudo determinar la ubicacion',16,1)
						return -1
					end 				
			
					set @Ubicaciones=1
				end else begin
					set @Ubicaciones=0
				end
				*/
			end else begin
				if exists (
					select 1 from (
					select u.almacen,u.número,count(*) as cantidad from ubicaciones as u inner join preextrproducto as p -- este count(*) también habrá que quitarlo en el futuro, lo dejo de momento solo por precaución
					on u.empresa=p.empresa and u.número=p.número and u.almacen=p.almacen
					left join linpedidovta as l on p.empresa=l.empresa and l.[nº orden]=p.linpedido 
					where   (l.picking<=0 or l.picking is null) and (u.empresa=@Empresa or u.empresa=@Espejo) and   p.diario=@Diario and p.cantidad<0 and u.estado=0 
					and ((p.pasillo is null or ltrim(rtrim(p.pasillo))='') or
					(p.fila is null or ltrim(rtrim(p.fila))='') or	
					(p.columna is null or ltrim(rtrim(p.columna))='') ) and (u.pasillo is not null  and u.fila is not null and u.columna is not null)
					group by u.almacen,u.número
					having count(*)>1) as a) begin
					if @@error!=0 begin
						raiserror ('No se pudo determinar la ubicacion',16,1)
						return -1
					end 	
			
					set @Ubicaciones=1
				end else begin
					set @Ubicaciones=0
				end	
				
				/*
				-- Carlos 30/11/23: refactorizo arriba
				if (
					select isnull(count(*),0) from (
					select u.almacen,u.número,count(*) as cantidad from ubicaciones as u inner join preextrproducto as p
					on u.empresa=p.empresa and u.número=p.número and u.almacen=p.almacen
					left join linpedidovta as l on p.empresa=l.empresa and l.[nº orden]=p.linpedido 
					where   (l.picking<=0 or l.picking is null) and (u.empresa=@Empresa or u.empresa=@Espejo) and   p.diario=@Diario and p.cantidad<0 and u.estado=0 
					and ((p.pasillo is null or ltrim(rtrim(p.pasillo))='') or
					(p.fila is null or ltrim(rtrim(p.fila))='') or	
					(p.columna is null or ltrim(rtrim(p.columna))='') ) and (u.pasillo is not null  and u.fila is not null and u.columna is not null)
					group by u.almacen,u.número
					having count(*)>1) as a)>=1 begin
					if @@error!=0 begin
						raiserror ('No se pudo determinar la ubicacion',16,1)
						return -1
					end 	
			
					set @Ubicaciones=1
				end else begin
					set @Ubicaciones=0
				end	
				*/
			end
			-- si hay mas de una ubicacion se mostramos un error	
			-- si no, asignamos esa ubicacion en preextracto producto
	
			if @ubicaciones>0 begin			
				raiserror ('No se puede realizar la acción. Hay salidas sin especificar la ubicacion. ',16,1)
				return -1
			end else begin
				update preextrproducto set pasillo=u.pasillo,fila=u.fila,columna=u.columna from ubicaciones as u inner join preextrproducto as p
					on u.número=p.número and u.almacen=p.almacen
					where u.estado=0 and p.diario=@diario 
						and ((p.pasillo is null or ltrim(rtrim(p.pasillo))='') or
						(p.fila is null or ltrim(rtrim(p.fila))='') or	
						(p.columna is null or ltrim(rtrim(p.columna))='') )
				if @@error!=0 begin
					raiserror ('No se pudo determinar la ubicacion',16,1)
					return -1
				end 	
				-- David Sanchez 21/06/04... tenemos que ver si se ha quedado algun producto sin ubicacion, y si fuera asi 
				-- le mostramos el error
				if ltrim(rtrim(@diario))!='_EntregFac' begin
					if exists (select 1 from preextrproducto as pr inner join productos as p on pr.empresa=p.empresa and pr.numero=p.numero
						 where pr.empresa=@empresa and pr.diario=@diario and p.ubicar=1 and pr.cantidad<0 
						and ((pasillo is null or ltrim(rtrim(pasillo))='') or
							(fila is null or ltrim(rtrim(fila))='') or	
							(columna is null or ltrim(rtrim(columna))='') )) begin
						raiserror ('No se pudo determinar la ubicacion',16,1)
						return -1
					end

					/*
					-- Carlos 30/11/23: refactorizo arriba
					if (select count(*) from preextrproducto as pr inner join productos as p on pr.empresa=p.empresa and pr.numero=p.numero
						 where pr.empresa=@empresa and pr.diario=@diario and p.ubicar=1 and pr.cantidad<0 
						and ((pasillo is null or ltrim(rtrim(pasillo))='') or
							(fila is null or ltrim(rtrim(fila))='') or	
							(columna is null or ltrim(rtrim(columna))='') ))> 0 begin
						raiserror ('No se pudo determinar la ubicacion',16,1)
						return -1
					end
					*/
				end	
			end
		end  -- cierra al primero de todos, al que si no tiene pasillo ni ubicacion
	end -- cierra al de que el diario sea disitinto de if ltrim(rtrim(@diario))<>'_RegInvent' begin
end -- cierra al if @AplicarUbicaciones=1
-- David Sanchez... 15/10/04.. Tenemos que ver si hay alguna ubicacion puesta con cantidad negativa en la que la cantidad
-- del diario sera mayor a la cantidad ubicada, ya que si no se quedaria en negativo
-- Cuando es una nota de entrega falla ya que siempre dice que hay mas cantidad en la linea q en la ubicacion
if @AplicarUbicaciones=1 begin
	if ltrim(rtrim(@diario))<>'_RegInvent'  and ltrim(rtrim(@diario))<>ltrim(rtrim(@diariorepo)) and ltrim(rtrim(@diario))<>'_MontarKit' and ltrim(rtrim(@diario))<>'_EntregFac'  and ltrim(rtrim(@diario))<>'_RecogFac' begin
	if exists (select 1 from (
			select p.numero,-p.cantidad as cantidad,isnull(u.cantidad,0) as cantidadubicada from preextrproducto as p
				left join ubicaciones as u on p.empresa=u.empresa 
				and (u.almacen=@Almacen or u.almacen is null) and (u.estado=0 or u.estado is null)
				and p.numero=u.numero and p.pasillo=u.pasillo and p.fila=u.fila and p.columna=u.columna
				inner join productos as pr on p.empresa=pr.empresa and p.numero=pr.numero
				left join almacenes as al on p.empresa=al.empresa and p.almacen=al.numero
				where (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null)) and diario=@Diario and p.cantidad<0  and pr.ubicar=1 and al.ControlUbicaciones=1
				) as a where cantidad>cantidadubicada) begin
	
			raiserror ('Hay más cantidad en la linea que en la ubicación',16,1)
			return -1
		end
		/*
		-- Carlos 30/11/23: refactorizo arriba
		if (select count(*) from (
			select p.numero,-p.cantidad as cantidad,isnull(u.cantidad,0) as cantidadubicada from preextrproducto as p
				left join ubicaciones as u on p.empresa=u.empresa 
				and (u.almacen=@Almacen or u.almacen is null) and (u.estado=0 or u.estado is null)
				and p.numero=u.numero and p.pasillo=u.pasillo and p.fila=u.fila and p.columna=u.columna
				inner join productos as pr on p.empresa=pr.empresa and p.numero=pr.numero
				left join almacenes as al on p.empresa=al.empresa and p.almacen=al.numero
				where (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null)) and diario=@Diario and p.cantidad<0  and pr.ubicar=1 and al.ControlUbicaciones=1
				) as a where cantidad>cantidadubicada)>0 begin
	
			raiserror ('Hay más cantidad en la linea que en la ubicación',16,1)
			return -1
		end
		*/
	end
end
-- David SAnchez 04/07/03
declare @StockNegativo as int

--SELECT * FROM PREEXTRPRODUCTO WHERE DIARIO=@diario
exec @StockNegativo=prdComprobarStockNegativo 1,@empresa,1,@diario
--set @stocknegativo = 1
if @stocknegativo<0 begin
	raiserror ('No se puede realizar la acción. Se quedaría un stock negativo. ',16,1)
	return -1
end
--- DAVID SANCHEZ 28/05/03 -----------------------------------------
-- LLAMAMOS A ACTUALIZAR LOS COSTES -----------------
-- para ello nos creamos una tabla temporal en la que guardaremos todos los datos que necesitamos para llamar al prdactualizapreciomedioproducto
-- necesitaremos ver si los productos tiene facuras de compra o no y si la tuvieran llamariamos al procedimiento, pero solo si el campo RecalculaPrecioMedio es uno
declare @CalculaPrecioMedio as bit
set @calculapreciomedio=(select RecalculaPrecioMedio from diariosproducto where empresa=@empresa and número=@diario)
if @calculapreciomedio!=0 begin
	declare @EmpresaEspejo as char(3)
	select @EmpresaEspejo = [IVA por defecto] from empresas where número=@Empresa
	-- Nos creamos la tabla temporal para insertar los productos
	if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#ProductosTemp') ) drop table [dbo].[#ProductosTemp]
	--despues las creamos
	CREATE TABLE [#ProductosTemp] 
	([Producto] [char] (15) not null,[FechaApunte] [dateTime],[FechaFacturaCmp] [datetime],[NºFactura] [int],[FechaAlbaran] [datetime]) 
	ON [PRIMARY]
	if @@error!=0 begin
		raiserror ('No se pudo crear la tabla',16,1)
		return -1
	end 	
	-- Insertamos los productos con las fechas que estamos mentiendo
	insert into #productostemp (producto,fechaapunte) select número,fecha from PreExtrProducto where empresa=@Empresa and diario = @diario and estado>=0 
	if @@error!=0 begin
		raiserror ('No se pudieron insertar lo productos para su actualización',16,1)
		return -1
	end 	
	-- Tendremos que buscar la maxima fecha de factura de compra que haya
	-- Lo haremos con un select max, para ver luego si hay alguna mayor a la que estamos haciendo
	update #productostemp set fechafacturacmp=l.fechafactura from linpedidocmp as l inner join #productostemp as p on l.producto=p.producto where l.empresa=@empresa  and l.tipolinea=1 and l.fechafactura>=p.fechaapunte
	if @@error!=0 begin
		raiserror ('No se pudo encontrar una fecha para la factura de compra',16,1)
		return -1
	end 	
	-- Borramos los que la fecha factura sean menores a la fecha del apunte
	delete #productostemp where fechafacturacmp<fechaapunte or fechafacturacmp is null
	if @@error!=0 begin
		raiserror ('No se pudieron borrar las líneas sin factura de compra',16,1)
		return -1
	end 	
	-- De los que se hayan quedado tendremos que poner el numero de la factura siguiente a la fecha que estamos haciendo
	update #productostemp set [nºfactura]=(select top 1 [nºfactura] from linpedidocmp where empresa = @empresa and estado>=4 and producto = #productostemp.producto and tipolinea = 1 and fechafactura>#productostemp.fechaapunte order by [fechafactura])
	if @@error!=0 begin
		raiserror ('No se pudo encontrar la siguiente factura de compra',16,1)
		return -1
	end 	
	-- Tendremos que buscar la fecha correspondiente al albaran que vamos ha hacer 
	update #productosTemp set fechaalbaran=(select min(fechaalbaran) from linpedidocmp where empresa=@empresa and producto=#productostemp.producto and nºfactura=#productostemp.nºfactura)
	if @@error!=0 begin
		raiserror ('No se pudo encontrar la fecha del albarán a actualizar.',16,1)
		return -1
	end 		
end
----------------------------------------------------------------------------------------
-----------------------------------------------------------------------------------------
-- COMIENZA TRANSACCIÓN
begin transaction
---- DAVID SANCHEZ  -- 28/05/03.. con la tabla que nos hemos creado hacemos la transacion
-- si la variable lo permite
if @calculapreciomedio!=0 begin
	declare @ProductoTabla as char(15)
	declare @FacturaTabla as int
	declare @DevueltoPrecioMedioProducto as smallint
	declare @FechaAlbaranTabla as datetime
	declare crsResto cursor fast_forward for select  producto,nºfactura,fechaAlbaran from #productostemp group by producto,nºfactura,FechaAlbaran
	open crsResto
	fetch next from crsResto into @ProductoTabla,@FacturaTabla,@FechaAlbaranTabla
	while @@fetch_status = 0 begin
				
		exec @DevueltoPrecioMedioProducto=prdActualizarPrecioMedioProducto @empresa,@empresaEspejo, @FacturaTabla,1,@ProductoTabla ,@FechaAlbaranTabla
		
		if @DevueltoPrecioMedioProducto<>1 begin -- es que ha salido con error del otro procedimiento con error
			raiserror ('No se pudieron actualizar los precios medios de los productos restantes',11,1) 
			close crsResto
			deallocate crsResto
			rollback
			return -1		
		end 
		
		fetch next from crsResto into @ProductoTabla,@FacturaTabla,@FechaAlbaranTabla
	end
	
	close crsResto
	deallocate crsResto
end
-----------------------------------------------------------------
--select * from PreExtrProducto where empresa=@Empresa and diario = @diario and estado>=0 /*09/09/02 Eduardo; No recoge los pendientes  cuando el estado es menor que 0*/
-- David Sanchez 22/06/04.. Si el diario es el de la reposicion,ya esta hecho todo
-- David Sanchez 21/10/04--- tenemos que filtrar por si esta haciendo un cambio de contacto
if @AplicarUbicaciones=1 begin
	if ltrim(rtrim(@diario))<>ltrim(rtrim(@diariorepo)) and ltrim(rtrim(@diario))<>'_RegInvent'  begin
		-- David Sanchez 18/05/04.... tenemos que insertar en la tabla ubicaciones los que tengan cantidad positiva
		-- y que el almacen tenga control de ubicaciones
		-- y que el pasillo sea nulo
		-- aqui filtramos tambien por los que el diario sera _EntFacCmp para que me inserte los recuperados
	
		-- David Sanchez 20/06/05 si es el diario de entrada de la repo le ponemos el numero de traspaso
		if ltrim(rtrim(@diario))=  ltrim(rtrim( @DiarioEntradaRepo)) begin
	
			insert into ubicaciones(empresa,almacen,número,cantidad,estado,NºTraspasoRepo, Usuario)
			select @empresa,p.almacen,p.número,sum(p.cantidad),2,p.[nºtraspaso], @Usuario 
			from preextrproducto as p inner join almacenes as a
				on p.empresa=a.empresa and p.almacen=a.número
				inner join productos as pr on p.empresa=pr.empresa and p.numero=pr.numero
				where p.estado>=0 and p.empresa=@empresa and a.controlubicaciones=1 and p.cantidad>0  and p.diario=@diario and (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_EntFacCmp' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null)) and p.pasillo is null and pr.ubicar=1
				group by p.almacen,p.número,p.[nºtraspaso]
			if @@error != 0 begin
				raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
				rollback
				return -1
			end
		end else if ltrim(rtrim(@Diario)) != '_MontarKit' begin -- Carlos 13/12/23: puesto lo de MontarKit para que no toque las ubicaciones (lo hace NestoAPI)
	
			insert into ubicaciones(empresa,almacen,número,cantidad,estado, Usuario)
			select @empresa,p.almacen,p.número,sum(p.cantidad),2, @Usuario 
				from preextrproducto as p inner join almacenes as a
				on p.empresa=a.empresa and p.almacen=a.número
				inner join productos as pr on p.empresa=pr.empresa and p.numero=pr.numero
				where p.estado>=0 and p.empresa=@empresa and a.controlubicaciones=1 and p.cantidad>0  and p.diario=@diario and (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_EntFacCmp' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null)) and p.pasillo is null and pr.ubicar=1
				group by p.almacen,p.número
			if @@error != 0 begin
				raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
				rollback
				return -1
			end
		end
		-- de los productos que la cantidad sea negativa, tendremos que hacer un update a la tabla ubicaciones
		-- mas que de que la cantidad sea negativa, de los que el pasillo, fila y columna no sea  null
		-- En el caso en que el diario sea '_EntregFac, puede ser la linea ya tenga picking, por lo que no tendremos que restarsela
		if ltrim(rtrim(@Diario))='_EntregFac' begin
			update ubicaciones set cantidad=u.cantidad+p.cantidad from ubicaciones as u inner join preextrproducto as p
				on u.número=p.número and u.fila=p.fila and u.columna=p.columna and u.pasillo=p.pasillo and u.almacen=p.almacen
				inner join linpedidovta as l
				on p.empresa=l.empresa and p.linpedido=l.[nº orden]
				where p.estado>=0 and p.diario=@diario and p.pasillo is not null and u.estado=0 and (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null))
				and (l.picking=0 or l.picking is null)
			if @@error != 0 begin
				raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
				rollback
				return -1
			end
			-- David Sánchez... 23/05/06..Para los que esten pendientes de colocar
			/* Carlos 22/10/26: no filtraba por almacén ni por empresa. Una entrega de Reina (sin control de ubicaciones)
			   restaba de la fila en estado 2 de Algete del mismo producto (41823, nota 928959 del 21/10/26). Y si había
			   varias filas en estado 2 restaba la cantidad entera en cada una. Ahora: solo almacenes con control de
			   ubicaciones, mismo almacén y empresa, y se reparte por las filas en estado 2 de la más antigua a la más nueva.
			update ubicaciones set cantidad=u.cantidad+p.cantidad from ubicaciones as u inner join preextrproducto as p
				on u.número=p.número and u.pasillo is null and u.estado=2
				inner join linpedidovta as l
				on p.empresa=l.empresa and p.linpedido=l.[nº orden]
				where p.estado>=0 and p.diario=@diario and p.pasillo is  null and u.estado=2  and (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null))
				and (l.picking=0 or l.picking is null)
			*/
			declare @E2Almacen as char(3)
			declare @E2Producto as char(15)
			declare @E2Cantidad as int
			declare @E2NºOrden as int
			declare @E2CantidadFila as int
			declare crsEstado2EntregFac cursor local fast_forward for
				select p.almacén, p.número, sum(p.cantidad)
				from preextrproducto as p inner join linpedidovta as l
				on p.empresa=l.empresa and p.linpedido=l.[nº orden]
				inner join almacenes as a
				on p.empresa=a.empresa and p.almacén=a.número
				where p.empresa=@empresa and p.estado>=0 and p.diario=@diario and p.pasillo is null and a.controlubicaciones=1
				and (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null))
				and (l.picking=0 or l.picking is null)
				group by p.almacén, p.número
				having sum(p.cantidad)<>0
			open crsEstado2EntregFac
			fetch next from crsEstado2EntregFac into @E2Almacen, @E2Producto, @E2Cantidad
			while @@fetch_status=0 begin
				if @E2Cantidad>0 begin
					-- entra: se suma a la fila en estado 2 más antigua (como antes, solo si existe)
					set @E2NºOrden=null
					select top 1 @E2NºOrden=nºorden from ubicaciones
						where almacén=@E2Almacen and número=@E2Producto and (empresa=@empresa or empresa=@espejo) and estado=2 and pasillo is null
						order by fechacreación asc, nºorden asc
					if @E2NºOrden is not null
						update ubicaciones set cantidad=cantidad+@E2Cantidad where nºorden=@E2NºOrden
					set @E2Cantidad=0
				end
				-- sale: se resta de las filas en estado 2, de la más antigua a la más nueva, sin dejar ninguna en negativo
				while @E2Cantidad<0 begin
					set @E2NºOrden=null
					select top 1 @E2NºOrden=nºorden, @E2CantidadFila=cantidad from ubicaciones
						where almacén=@E2Almacen and número=@E2Producto and (empresa=@empresa or empresa=@espejo) and estado=2 and pasillo is null and cantidad>0
						order by fechacreación asc, nºorden asc
					if @E2NºOrden is null begin
						set @E2Cantidad=0
					end else if @E2CantidadFila>=-@E2Cantidad begin
						update ubicaciones set cantidad=cantidad+@E2Cantidad where nºorden=@E2NºOrden
						set @E2Cantidad=0
					end else begin
						update ubicaciones set cantidad=0 where nºorden=@E2NºOrden
						set @E2Cantidad=@E2Cantidad+@E2CantidadFila
					end
					if @@error != 0 begin
						close crsEstado2EntregFac
						deallocate crsEstado2EntregFac
						raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
						rollback
						return -1
					end
				end
				fetch next from crsEstado2EntregFac into @E2Almacen, @E2Producto, @E2Cantidad
			end
			close crsEstado2EntregFac
			deallocate crsEstado2EntregFac
			if @@error != 0 begin
				raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
				rollback
				return -1
			end
			delete ubicaciones where estado=2 and cantidad=0
			if @@error != 0 begin
				raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
				rollback
				return -1
			end
			-- si el diario es el de entregar factura, ponemos a -3 los posibles que ubiera con picking
			-- filtramos tambien por el numero de orden del pedido (en el join) para que no me actualice todas
			update ubicaciones set estado=-3  from ubicaciones as u inner join preextrproducto as p
				on u.empresa=p.empresa and u.numero=p.numero and u.almacen=p.almacen and u.nºordenvta=p.linpedido
					where p.empresa=@Empresa and p.diario=@Diario and u.estado=3 and p.estado>=0 
	
			if @@error != 0 begin
				raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
				rollback
				return -1
			end
		end else begin
			if  ltrim(rtrim(@diario))<>'_MontarKit' begin
				update ubicaciones set cantidad=u.cantidad+p.cantidad from ubicaciones as u inner join preextrproducto as p
					on u.número=p.número and u.fila=p.fila and u.columna=p.columna and u.pasillo=p.pasillo and u.almacen=p.almacen
					where p.diario=@diario and p.pasillo is not null and u.estado=0 and (p.linpedido is null or (p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null))
				if @@error != 0 begin
					raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
					rollback
					return -1
				end
			end
		end
		if  ltrim(rtrim(@diario))<>'_MontarKit' begin
			-- tendremos que insertar en ubicaciones los que no existan
			insert into ubicaciones (empresa,almacen,número,cantidad,pasillo,fila,columna,estado, Usuario)
				select @empresa,p.almacen,p.numero,sum(p.cantidad),p.pasillo,p.fila,p.columna,0, @Usuario
				from preextrproducto as p inner join almacenes as a
				on p.empresa=a.empresa and p.almacen=a.número
				left join ubicaciones as u
				on u.empresa=p.empresa and u.numero=p.numero and u.pasillo=p.pasillo and u.fila=p.fila and u.columna=p.columna and u.estado=0
				inner join productos as pr
				on p.empresa=pr.empresa and p.numero=pr.numero
				where pr.ubicar=1 and p.empresa=@empresa and a.controlubicaciones=1 and p.diario=@diario and (p.linpedido is null or(p.linpedido is not null and  p.diario='_EntregFac' and p.albaran is null) or (p.linpedido is not null and  p.diario='_RecogFac' and p.albaran is null)) and p.pasillo is not null  and u.pasillo is null 
				group by p.almacen,p.número,p.pasillo,p.fila,p.columna
			if @@error != 0 begin
				raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
				rollback
				return -1
			end
		end
		-- borramos las ubicaciones que el estado sea cero y la cantidad sea cero tambien
		delete ubicaciones where estado=0 and cantidad=0
		if @@error != 0 begin
			raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
			rollback
			return -1
		end
	end
end -- cierra al if @AplicarUbicaciones=1
/*
insert into ExtractoProducto (empresa,número,fecha,[nº cliente],contactocliente,nºproveedor,contactoproveedor,albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,Vendedor,NºTraspaso,NºPedido,diario,centrocoste,departamento) 
	select Empresa,número,fecha,[nº cliente],Contactocliente,nºproveedor,contactoproveedor,albarán,texto,almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,Vendedor,NºTraspaso,NºPedido,diario,centrocoste,departamento from PreExtrProducto where empresa=@Empresa and diario = @diario and estado>=0 /*09/09/02 Eduardo; No recoge los pendientes  cuando el estado es menor que 0*/
if @@error != 0 begin
	raiserror('No se ha podido pasar el extracto del producto a definitivo',11,1)
	rollback
	return -1
end
*/
INSERT INTO ExtractoProducto (
    empresa, número, fecha, [nº cliente], contactocliente, nºproveedor, contactoproveedor,
    albarán, texto, almacén, grupo, cantidad, importe, delegación, [forma venta],
    [asiento automático], linpedido, Vendedor, NºTraspaso, NºPedido, diario, centrocoste,
    departamento, Usuario
)
SELECT
    Empresa, número, fecha, [nº cliente], Contactocliente, nºproveedor, contactoproveedor,
    albarán, texto, almacén, grupo, cantidad, importe, delegación, [forma venta],
    [asiento automático], linpedido, Vendedor, NºTraspaso, NºPedido, diario, centrocoste,
    departamento, @Usuario
    --CASE WHEN Usuario IS NULL OR CHARINDEX('$', SUSER_NAME()) = 0 THEN SUSER_NAME() ELSE Usuario END AS Usuario
FROM
    PreExtrProducto
WHERE
    empresa = @Empresa AND diario = @diario AND estado >= 0;



-- Carlos 13/12/23
declare @resultado as int 
set @resultado = isnull(@@identity,1)

-- David Sanchez 15/06/04
-- Antes de borrar del preextracto, miramos los que el estado sean Aanular, y los pasamos a estado -1
declare @EstadoAAnular as int
declare @EstadoNulo as int
--declare @NumAModificar as int --> Carlos 04/10/05 Lo pongo como char porque sino da error al igualarlo a productos.numero
declare @NumAModificar as char(15)
set @EstadoNulo = -1
set @EstadoAAnular=(select top 1 numero from estadosproducto where Empresa = @Empresa AND AAnular=1)
if @@Error!=0 begin
	raiserror('No se han podido actualizar los estados de los productos a anular',11,1)
	rollback
	return -1		
end 

UPDATE pr
SET estado = @EstadoNulo
FROM productos pr
WHERE EXISTS (
    SELECT 1
    FROM extractoproducto e INNER JOIN preextrproducto p 
	ON e.empresa IN (@Empresa, @EmpresaEspejo) AND p.Empresa = @Empresa AND p.numero = e.numero AND p.almacen = e.almacen
    WHERE e.empresa IN (@Empresa, @EmpresaEspejo) AND p.Empresa = @Empresa AND e.numero = pr.numero AND p.diario = @Diario AND pr.estado = @EstadoAAnular 
    GROUP BY e.numero
    HAVING SUM(e.cantidad) <= 0
)

IF @@ERROR != 0
BEGIN
    RAISERROR('No se han podido actualizar los estados de los productos a anular', 11, 1)
    ROLLBACK
    RETURN -1
END

/*
-- Carlos: 30/11/23 refactorizado en el código de arriba
if @Espejo is null begin
	declare crsEstado cursor fast_forward for   
		select pr.número from  productos as pr inner join extractoproducto as e
		on pr.empresa=e.empresa and pr.numero=e.numero
		inner join preextrproducto as p
		on p.empresa=e.empresa and p.número=e.número and p.almacen=e.almacen	
		where e.empresa=@empresa  and p.diario=@diario and  pr.estado=@EstadoAAnular
		group by pr.número,p.almacen
		having sum(e.cantidad)=0
	if @@Error!=0 begin
		raiserror('No se han podido actualizar los estados de los productos a anular',11,1)
		rollback
		return -1		
	end 
end else begin
	declare crsEstado cursor fast_forward for   
		select pr.número from  productos as pr inner join extractoproducto as e
		on pr.empresa=e.empresa and pr.numero=e.numero
		inner join preextrproducto as p
		on p.empresa=e.empresa and p.número=e.número and p.almacen=e.almacen	
		where (e.empresa=@empresa or e.empresa=@Espejo)  and p.diario=@diario and  pr.estado=@EstadoAAnular
		group by pr.número,p.almacen
		having sum(e.cantidad)=0
	if @@Error!=0 begin
		raiserror('No se han podido actualizar los estados de los productos a anular',11,1)
		rollback
		return -1		
	end 
end 
open crsEstado
fetch next from crsEstado into @NumAModificar
while @@fetch_status = 0 begin
	update productos set estado=-1 where empresa=@empresa and número=@NumAModificar
	if @@Error!=0 begin
		raiserror('No se han podido actualizar los estados de los productos a anular',11,1)
		close crsEstado
		deallocate crsEstado
		rollback
		return -1		
	end 
fetch next from crsEstado into @NumAModificar
end
close crsEstado
deallocate crsEstado
*/

-- David Sanchez Lopez.. 15/06/04
-- De igual manera, los que esten en estado -1 y se queden con stock, los pondremos a estado 4

UPDATE pr
SET estado = @EstadoAAnular
FROM productos pr
WHERE EXISTS (
    SELECT 1
    FROM extractoproducto e INNER JOIN preextrproducto p 
	ON e.empresa IN (@Empresa, @EmpresaEspejo) AND p.Empresa = @Empresa AND p.numero = e.numero AND p.almacen = e.almacen
    WHERE e.empresa IN (@Empresa, @EmpresaEspejo) AND p.Empresa = @Empresa AND e.numero = pr.numero AND p.diario = @Diario AND pr.estado = @EstadoNulo 
    GROUP BY e.numero
    HAVING SUM(e.cantidad) > 0
)

IF @@ERROR != 0
BEGIN
    RAISERROR('No se han podido actualizar los estados de los productos a recuperar', 11, 1)
    ROLLBACK
    RETURN -1
END

/*
-- Carlos 30/11/23: refactorizamos el código arriba
if @espejo is null begin
	declare crsEstado2 cursor fast_forward for   
		select pr.número from  productos as pr inner join extractoproducto as e
		on pr.empresa=e.empresa and pr.numero=e.numero
		inner join preextrproducto as p
		on p.empresa=e.empresa and p.número=e.número and p.almacen=e.almacen	
		where e.empresa=@empresa   and p.diario=@diario and  pr.estado=-1
		group by pr.número,p.almacen
		having sum(e.cantidad)>0
	if @@Error!=0 begin
		raiserror('No se han podido actualizar los estados de los productos a anular',11,1)
		rollback
		return -1		
	end 
end else begin
	declare crsEstado2 cursor fast_forward for   
		select pr.número from  productos as pr inner join extractoproducto as e
		on pr.empresa=e.empresa and pr.numero=e.numero
		inner join preextrproducto as p
		on p.empresa=e.empresa and p.número=e.número and p.almacen=e.almacen	
		where (e.empresa=@empresa or e.empresa=@Espejo)  and p.diario=@diario and  pr.estado=-1
		group by pr.número,p.almacen
		having sum(e.cantidad)>0
	if @@Error!=0 begin
		raiserror('No se han podido actualizar los estados de los productos a anular',11,1)
		rollback
		return -1		
	end 
end 
open crsEstado2
fetch next from crsEstado2 into @NumAModificar
while @@fetch_status = 0 begin
	update productos set estado=@Estado where empresa=@empresa and número=@NumAModificar
	if @@Error!=0 begin
		raiserror('No se han podido actualizar los estados de los productos a anular',11,1)
		close crsEstado2
		deallocate crsEstado2
		rollback
		return -1		
	end 
	fetch next from crsEstado2 into @NumAModificar
end
close crsEstado2
deallocate crsEstado2
*/
delete from preextrproducto where empresa=@empresa and diario = @diario and estado>=0 /* 09/09/02 Eduardo; Ponemos el filtro estado */
if @@error != 0 begin
	raiserror('No se han podido eliminar las líneas del pre-extracto de producto',11,1)
	rollback
	return -1
end
commit transaction
return @resultado
-- FIN TRANSACCIÓN

GO
