-- UbicacionesDeshacerAlbaranEspejo (propuesta 11/11/26, NO aplicado): prdDeshacerExtractoProducto devuelve también las
-- ubicaciones de un albarán creado en la empresa 1 y traspasado a la espejo (EmpresaAlbaranVta = 1, cabecera en la 3).
-- Ejecutar como sa en NV. Idempotente: es un ALTER del cuerpo completo (relanzarlo deja lo mismo).
-- Marcha atrás: Rollback_prdDeshacerExtractoProducto.sql (la definición de 2010 tal cual).
USE NV;
GO
IF OBJECT_DEFINITION(OBJECT_ID('dbo.prdDeshacerExtractoProducto')) LIKE '%UbicacionesDeshacerAlbaranEspejo%'
    PRINT 'Ya estaba aplicado: se vuelve a dejar igual.';
GO
ALTER PROCEDURE prdDeshacerExtractoProducto @Empresa as char(3),@Tipo as int,@Número as integer  AS
/*
	David Sanchez Lopez.. 04/07/03
	
Procedimiento que me borra de extracto producto
Parametros:
	Empresa--> Empresa desde la que llamo al procedimiento
	Tipo--> Tipo de apunte que quiero deshacer. Será:
		1 - Albarán Venta
		2 - Albarán Compra
		3 - Nº Traspaso
	Número--> Numero de albaran o de traspaso que deseho deshacer
*/
declare @Devuelto as int
declare @Almacen as char (3)
declare @Cantidad as integer
declare @Pasillo as char (3)
declare @fila as char (3)
declare @Columna as char (3)
declare @Producto as char (10)
declare @Estado as int
declare @ProductoUbicacion as char(10)
set @ProductoUbicacion='12375'
-- Iniciamos la transacion
begin transaction
if @Tipo=1 begin -- Albaran de ventas
	-- Primero tendremos que ver si el stock se quedaria en negativo
	 exec @devuelto=prdComprobarStockNegativo 2,@empresa,@tipo,@número
	if @devuelto<0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto por que se quedaria un stock negativo',16,1) 
		return -1
	end
	
	-- UbicacionesDeshacerAlbaranEspejo (11/11/26, albaranes 732939/732940): la facturación de rutas albaranea en la empresa 1
	-- (Ubicaciones.EmpresaAlbaranVta = 1) y luego traspasa líneas y CabAlbaránVta a la espejo (3) sin tocar Ubicaciones.
	-- Al deshacer en la 3, el cursor de abajo (empresaAlbaranVta=@empresa) no veía esas filas: la reserva (-3) o la salida (-1)
	-- se quedaban sin devolver y el hueco descuadrado. Las filas de este albarán cuya EmpresaAlbaranVta apunta a una cabecera
	-- que ya no existe son de ESTA cabecera (el contador de albaranes es global): se les pone la empresa del albarán.
	if exists (select * from cabalbaránvta where empresa=@Empresa and número=@Número) begin
		update ubicaciones set empresaAlbaranVta=@Empresa
			where albaranvta=@Número and empresaAlbaranVta is not null and empresaAlbaranVta<>@Empresa
			and not exists (select * from cabalbaránvta as c where c.empresa=ubicaciones.empresaAlbaranVta and c.número=@Número)
		if @@error!=0 begin
			rollback
			raiserror ('No se pudo Borrar de extracto producto',16,1) 
			return -1
		end
	end
	-- David Sanchez 19/05/04
	-- no se puede deshacer un albaran con lineas en estado -2 en ubicaciones, debe colocarlo antes
	-- David Sanchez 31/05/04.. lo que este con estado 2
	if (select count(*) from ubicaciones as u inner join productos as p on u.empresa=p.empresa and u.numero=p.numero where p.ubicar=1 and u.albaranvta=@Número and u.empresaAlbaranVta=@empresa and u.pasillo is null and u.estado>=0)>0 begin
		rollback
		raiserror ('No se puede borrar el albarán porque hay productos del mismo pendientes de ubicar',16,1) 
		return -1
	end 
	
	-- David Sanchez 01/08/08 ... No podemos deshacer un albaran que tienen lineas reservadas para picking de ese albaran, ya que
	-- el stock bajaria y el picking se quedaria reservado
	if (select count(*) from ubicaciones where albaranvta=@Número  and estado=3)>0 begin
		rollback
		raiserror ('No puede deshacer el albarán ya que tiene lineas reservadas para picking.',16,1) 
		return -1
	end 


	-- David Sanchez 19/05/04... Nos abrimos el cursor
	-- tendremos que buscar si existe esta ubicacion, si existiera, la actualizamos
	-- y borramos esta linea, si no actualizamos esta misma linea
	-- tenemos que buscar el almacen
	set @almacen=(select almacen from linpedidovta where empresa=@Empresa and [nº albaran]=@Número group by almacen)
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end	
	declare crsUbicaciones  cursor local dynamic for select número,cantidad,pasillo,fila,columna,estado from ubicaciones where albaranvta=@número and empresaAlbaranVta=@empresa and almacen=@almacen
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
	open crsUbicaciones
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
	fetch next from crsUbicaciones into @Producto,@cantidad,@pasillo,@fila,@columna,@estado
	while @@fetch_status=0 begin
		-- buscamos si existe
		-- si el estado es -3 es que es de picking, por lo que lo unico que tendremos que hacer es pasarlo a estado 3, multiplicar la cantidad por -1 y quitarle el numero de albaran
		if @estado=-3 begin
			-- actualizamos para volver al estado albaran
			update ubicaciones set EmpresaAlbaranVta=null,AlbaránVta=null,estado=3,cantidad=-1*cantidad where current of crsUbicaciones
			if @@error!=0 begin
				rollback
				raiserror ('No se pudo Borrar de extracto producto',16,1) 
				return -1
			end
		end else begin
			-- David Sanchez... 13/10/04
			-- Me piden que al deshacer un albaran, lo deje siempre pendiente de ubicar
			if @Cantidad<0 begin
				update ubicaciones set cantidad=-1*cantidad,albaranvta=null,nºordenvta=null,estado=2,empresaAlbaranVta=null,pasillo=null,fila=null,columna=null  where current of crsUbicaciones
				if @@error!=0 begin
					rollback
					raiserror ('No se pudo Borrar de extracto producto',16,1) 
					return -1
				end
			end else begin
			
				if (select count(numero) from ubicaciones where  almacen=@almacen and  número=@Producto and pasillo=@pasillo and columna=@columna and fila=@fila and estado=0 and pedidovta is null and albaranvta is null )>0 begin
					-- si existe tendremos que actualizar el que esta y borrar este
					update ubicaciones set cantidad=cantidad - @cantidad where   almacen=@almacen and numero=@Producto and pasillo=@pasillo and columna=@columna and fila=@fila and estado=0 and pedidovta is null and albaranvta is null
					if @@error!=0 begin
						rollback
						raiserror ('No se pudo Borrar de extracto producto',16,1) 
						return -1
					end
					-- despues borramos este
					delete ubicaciones where current of crsUbicaciones
					if @@error!=0 begin
						rollback
						raiserror ('No se pudo Borrar de extracto producto',16,1) 
						return -1
					end
		
				end else begin
					-- si no existe simplemente modificamos este
					update ubicaciones set cantidad=-1*cantidad,pedidovta=null,albaranvta=null,nºordenvta=null,estado=0,empresaAlbaranVta=null  where current of crsUbicaciones
					if @@error!=0 begin
						rollback
						raiserror ('No se pudo Borrar de extracto producto',16,1) 
						return -1
					end
				end 
			end			
		end
	fetch next from crsUbicaciones into @Producto,@cantidad,@pasillo,@fila,@columna,@estado
	end
	close crsUbicaciones
	deallocate crsUbicaciones
	-- UbicacionesDeshacerAlbaranEspejo: red de seguridad. Si alguna fila de Ubicaciones de este albarán sigue apuntando a él
	-- (y a esta empresa), no se deshace: el stock volvería y la ubicación no.
	if exists (select * from ubicaciones where albaranvta=@Número and empresaAlbaranVta=@Empresa and estado<0) begin
		rollback
		raiserror ('No se puede deshacer el albarán %d: hay ubicaciones suyas que no se han podido devolver. Avise a Informática.',16,1,@Número) 
		return -1
	end
	-- borramos los que se han quedado a cero
	delete ubicaciones where cantidad=0 and estado=0	
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
	delete extractoproducto from extractoproducto as e inner join linpedidovta as l on e.linpedido=l.[nº orden] where albaran=@número and l.empresa=@Empresa and e.[nº cliente] is not null
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
end else if @Tipo=2 begin -- Albaran de compras

	-- David Sanchez... 19/09/07 ... Si el albaran que estamos desaciendo no coincide en el extracto totalmente con la linea no se lo dejamos deshacer.
	-- Esto es porque se han dado una serie de errores al dividir lienas albaraneadas, ya que les cambia el numero de albaran
	
	if (select count(*) from linpedidocmp where empresa=@empresa  and nºalbaran=@numero and tipolinea=1)>=1 begin -- comprobamos que haya lineas de producto
		if  (select count(*) from extractoproducto as e inner join linpedidocmp as l on e.linpedido=l.[nºorden]
			and l.cantidad=e.cantidad
				 where albaran=@Numero and l.empresa=@Empresa  and e.nºproveedor is not null)=0 begin
			rollback
			raiserror ('No se puede deshacer  ya que el albaran ha sido dividido',16,1) 
			return -1
	
		end
	end
	
             exec @devuelto=prdComprobarStockNegativo 2,@empresa,@tipo,@número
	if @devuelto<0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto por que se quedaria un stock negativo',16,1) 
		return -1
	end
	-- David Sanchez Lopez.. 26/01/05... Tenemos que comprobar al deshacer si lo que se queda en ubicaciones sin ubicar
	-- es igual al albaran que estamos deshaciendo, si fuera asi directamente lo borramos para que no tenga que ubicarlo 
	-- antes de borrar
	if (select count (*) from (
		select l.producto from linpedidocmp as l inner join productos as p
			on l.empresa=p.empresa and l.producto=p.numero
			inner join ubicaciones as u
			on u.numero=l.producto and u.albarancmp=l.nºalbaran
			where l.nºalbaran=@Número and  l.cantidad>0 and p.ubicar=1 and u.estado=2
			group by l.producto
				having sum(l.cantidad)!=sum(u.cantidad)) as a)=0 begin
		-- borramos las lineas
		delete ubicaciones from linpedidocmp as l inner join productos as p
			on l.empresa=p.empresa and l.producto=p.numero
			inner join ubicaciones as u
			on u.numero=l.producto and u.albarancmp=l.nºalbaran
			where l.nºalbaran=@Número and  l.cantidad>0  and p.ubicar=1 and u.estado=2
			if @@error!=0 begin
				rollback
				raiserror ('No se pudo Borrar de las ubicaciones',16,1) 
				return -1
			end
	end	
	-- no se puede deshacer un albaran con lineas en estado -2 en ubicaciones, debe colocarlo antes
	-- David Sanchez... Buscamos por estado 2
	if (select count(*) from ubicaciones where albarancmp=@Número and empresaAlbaranCmp=@empresa and estado=2)>0 begin
		rollback
		raiserror ('No se puede borrar el albarán porque hay productos del mismo pendientes de ubicar',16,1) 
		return -1
	end 

	-- David Sanchez 30/07/08 ... No podemos deshacer un albaran que tienen lineas reservadas para picking de ese albaran, ya que
	-- el stock bajaria y el picking se quedaria reservado
	if (select count(*) from ubicaciones where albarancmp=@Número  and estado=3)>0 begin
		rollback
		raiserror ('No puede deshacer el albarán ya que tiene lineas reservadas para picking.',16,1) 
		return -1
	end 

	-- David Sanchez 20/05/04... Nos abrimos el cursor
	-- tendremos que buscar si existe esta ubicacion, si existiera, la actualizamos
	-- y borramos esta linea, si no actualizamos esta misma linea
	-- tenemos que buscar el almacen
	set @almacen=(select top 1 almacen from linpedidocmp where empresa=@Empresa and [nºalbaran]=@Número group by almacen)
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end	
	declare crsUbicaciones  cursor local dynamic for select número,cantidad,pasillo,fila,columna from ubicaciones where albarancmp=@número and empresaAlbaranCmp=@empresa and almacen=@almacen
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
	open crsUbicaciones
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
	fetch next from crsUbicaciones into @Producto,@cantidad,@pasillo,@fila,@columna
	while @@fetch_status=0 begin
		-- buscamos si existe
	
		if (select count(numero) from ubicaciones where almacen=@almacen and  número=@Producto and pasillo=@pasillo and columna=@columna and fila=@fila and estado=0 and pedidocmp is null and albarancmp is null )>0 begin
			-- si existe tendremos que actualizar el que esta y borrar este
			update ubicaciones set cantidad=cantidad - @cantidad where   almacen=@almacen and numero=@Producto and pasillo=@pasillo and columna=@columna and fila=@fila and estado=0 and pedidocmp is null and albarancmp is null
			if @@error!=0 begin
				rollback
				raiserror ('No se pudo Borrar de extracto producto',16,1) 
				return -1
			end
			-- despues borramos este
			delete ubicaciones where current of crsUbicaciones
			if @@error!=0 begin
				rollback
				raiserror ('No se pudo Borrar de extracto producto',16,1) 
				return -1
			end
		
		end else begin
			-- si no existe simplemente modificamos este
			if @Cantidad>0 begin
				update ubicaciones set cantidad=cantidad,pedidocmp=null,albarancmp=null,nºordencmp=null,estado=0,empresaAlbaranCmp=null where current of crsUbicaciones
				if @@error!=0 begin
					rollback
					raiserror ('No se pudo Borrar de extracto producto',16,1) 
					return -1
				end
			end else begin
				-- si la cantidad es menor de cero es que ha sido de un abono, lo dejamos pendiente de entrada
				update ubicaciones set cantidad=-cantidad,pasillo=null,fila=null,columna=null,pedidocmp=null,albarancmp=null,nºordencmp=null,estado=2,empresaAlbaranCmp=null where current of crsUbicaciones
				if @@error!=0 begin
					rollback
					raiserror ('No se pudo Borrar de extracto producto',16,1) 
					return -1
				end
			end
		end 
	fetch next from crsUbicaciones into @Producto,@cantidad,@pasillo,@fila,@columna
	end
	close crsUbicaciones
	deallocate crsUbicaciones
	delete ubicaciones where cantidad=0 and estado=0	
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
	delete extractoproducto from extractoproducto as e inner join linpedidocmp as l on e.linpedido=l.[nºorden] where albaran=@Número and l.empresa=@Empresa and e.nºproveedor is not null
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
end else if @Tipo=3 begin--- Para deshacer una reposicion
	 exec @devuelto=prdComprobarStockNegativo 2,@empresa,@tipo,@número
	if @devuelto<0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto por que se quedaria un stock negativo',16,1) 
		return -1
	end
	delete extractoproducto where empresa=@empresa and NºTraspaso=@Número
	if @@error!=0 begin
		rollback
		raiserror ('No se pudo Borrar de extracto producto',16,1) 
		return -1
	end
end
commit transaction
return 1
GO
