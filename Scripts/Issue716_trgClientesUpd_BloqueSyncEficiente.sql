/*
    Issue716 (27/10/26): trgClientesUpd, último bloque (el que apunta en Nesto_sync cuando el cambio NO viene de la API).

    Qué cambia (solo ese bloque; el resto del trigger va tal cual estaba en producción el 27/10/26 a las 21:50):
      1) Solo entra si alguna de las 12 columnas que se sincronizan va en el SET del UPDATE (UPDATE(col) OR ...).
         Un backfill de FechaAlta/UsuarioAlta/AplicacionAlta ya no lo dispara.
      2) El IF EXISTS lleva OPTION (HASH JOIN): inserted y deleted no tienen índice y, con miles de filas, el optimizador
         elegía un bucle anidado de N x N (55.000 x 55.000 = más de 3.000 millones de comparaciones, más de 30 minutos
         con la tabla Clientes bloqueada; la gente se quejó y la sesión acabó deshaciéndose).
    Semántica idéntica para los cambios de uno en uno desde Nesto: mismo INSERT en Nesto_sync cuando cambia algo.

    Ejecutar en SSMS contra NV como sa. Después, comprobar con un cambio de nombre de un cliente de prueba desde Nesto que
    sigue apareciendo la fila en Nesto_sync.
*/
USE NV;
GO
ALTER TRIGGER [dbo].[trgClientesUpd] ON [dbo].[Clientes] 
FOR insert, UPDATE
AS
SET NOCOUNT ON;
if update([CIF/NIF]) begin
	/* ---- Bloqueo de versiones obsoletas de Nesto con CIF/NIF largos ---- */
	if ORIGINAL_LOGIN() <> 'NUEVAVISION\RDS2016$'
	   and exists (
			select 1
			from inserted as i
			inner join deleted as d
				on  d.empresa       = i.empresa
				and d.[nº cliente]  = i.[nº cliente]
				and d.contacto      = i.contacto
			where isnull(i.[CIF/NIF], '') <> isnull(d.[CIF/NIF], '')
			  and ( len(ltrim(rtrim(isnull(i.[CIF/NIF], '')))) > 9
			     or len(ltrim(rtrim(isnull(d.[CIF/NIF], '')))) > 9 )
	   ) begin
		raiserror ('Está usando una versión obsoleta de Nesto que no puede modificar este CIF/NIF (más de 9 caracteres). Debe hacerse desde una versión más reciente de Nesto.', 11, 1)
		rollback
		return
	end

	-- Carlos, 08/09/26: antes bastaba con que [CIF/NIF] fuera en el SET del UPDATE, y la API
	-- guarda el cliente entero (EntityState.Modified), asi que esto saltaba en CUALQUIER
	-- modificacion de la ficha aunque el NIF no cambiara: 26 clientes con NIF duplicado se
	-- quedaban sin poder tocar ni la direccion ni un CCC. Ahora se compara inserted contra
	-- deleted, igual que ya hace la comprobacion del NIF largo de aqui arriba.
	if exists (
			select 1
			from inserted as i
			left join deleted as d
				on  d.empresa      = i.empresa
				and d.[nº cliente] = i.[nº cliente]
				and d.contacto     = i.contacto
			-- LEFT y no INNER: el disparador tambien es FOR INSERT, y en un alta `deleted`
			-- esta vacia. Con INNER la comprobacion no corria en las altas y se podia crear
			-- un cliente con el NIF de otro (agujero del 08/09/26, corregido el mismo dia).
			where (d.empresa is null                                          -- es un ALTA
			       or isnull(i.[CIF/NIF], '') <> isnull(d.[CIF/NIF], ''))     -- o el NIF cambia
			  and exists (select 1 from clientes c
			              where c.estado >= 0
			                and c.empresa = i.empresa
			                and c.[nº cliente] != i.[nº cliente]
			                and c.[CIF/NIF] = i.[CIF/NIF])
	) begin
		raiserror ('Ya existe un cliente con ese CIF/NIF', 11, 1)
		rollback
	end else begin
/*		declare @nif as char(10)
		declare @cliente as char (10)
		declare @contacto as char(3)
		declare @empresa as char(3)
		set @nif=(select distinct([cif/nif]) from inserted)
		set @cliente=(select distinct([nº cliente]) from inserted)
		set @contacto=(select contacto from inserted)
		set @empresa=(select distinct(empresa) from inserted)
		update clientes set [cif/nif]=@nif where empresa=@empresa and  [nº cliente]=@cliente and contacto!= @contacto*/
		if @@error!=0 begin
			raiserror ('Error actualizando el  CIF/NIF', 11, 1)
			rollback
		end
	end
	/*
	DECLARE @empresa as char(3)
	set @empresa=(select top 1 empresa from inserted)
	if ltrim(rtrim(@empresa))<>'3' begin
		if (select top 1 numero  from extractocliente as e inner join inserted as i on e.empresa=i.empresa and  e.numero=i.[nº cliente] and e.contacto=i.contacto) is not null  begin
			raiserror ('No se puede modificar el nif de un cliente con extracto.', 11, 1)
			rollback
		end 
	end
	*/
end
 if update(ccc) begin
	declare @EmpresaCCC as char(3)
	declare @CliCCC as char (10)
	declare @ContactoCCC as char(3)
	declare @CCCAnt as char(3)
	declare @CCCNue as char(3)	
	set @CliCCC=(select top 1 [nº cliente] from inserted)
	set @EmpresaCCC=(select top 1 empresa from inserted)
	set @ContactoCCC=(select top 1 Contacto from inserted)
	
	-- buscamos el ccc antiguo
	set @CCCAnt =(select top 1 ccc from deleted)
	-- buscamos el ccc nuevo
	set @CCCNue=(select top 1 ccc from inserted)
	
	-- si son distintos actualizamos
	if @CCCAnt is null and @cccNue is not null begin
			-- insertamos en la tabla log modificaciones
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			values (@EmpresaCCC,'Clientes','Actualizar',@CliCCC,'CCC',getdate(),@CCCAnt)
			if @@error!=0 begin
				raiserror ('Error actualizando el  CCC', 11, 1)
				rollback
			end	
	end else  if @CCCAnt is not null and @cccNue is  null begin
		-- insertamos en la tabla log modificaciones
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			values (@EmpresaCCC,'Clientes','Actualizar',@CliCCC,'CCC',getdate(),@CCCAnt)
			if @@error!=0 begin
				raiserror ('Error actualizando el  CCC', 11, 1)
				rollback
			end	
	end else  if @CCCAnt<> @cccNue begin
		-- insertamos en la tabla log modificaciones
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			values (@EmpresaCCC,'Clientes','Actualizar',@CliCCC,'CCC',getdate(),@CCCAnt)
			if @@error!=0 begin
				raiserror ('Error actualizando el  CCC', 11, 1)
				rollback
			end	
	end
end 
 if update(estado) begin
	if (select isnull(sum(importepdte),0)  from extractocliente as e inner join inserted as i on e.empresa=i.empresa and e.numero=i.[nº cliente] and e.contacto=i.contacto and i.estado=-1)<>0 begin
		raiserror ('No puede anular un cliente con saldo.', 11, 1)
		rollback
	end

	-- Carlos 16/01/25: guardamos un registro de cambios de estado para poder sacar ventas históricas por estado
	INSERT INTO SeguimientoCambiosEstadoCliente 
        (empresa, cliente, contacto, estadoanterior, estadonuevo)
    SELECT 
        i.empresa,
        i.[Nº Cliente],
        i.contacto,
        d.estado AS estadoanterior,
        i.estado AS estadonuevo
    FROM inserted i INNER JOIN deleted d 
	ON i.Empresa = d.Empresa and i.[Nº Cliente] = d.[Nº Cliente] AND i.Contacto = d.Contacto
    WHERE i.estado <> d.estado

	-- David Sanchez 30/06/04
	-- Ponemos el insert con una select porque si no no se podia actualizar varios a la vez.
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			select i.empresa,'Clientes','Actualizar',i.[nº cliente],'Estado',getdate(),d.estado
				from inserted as i inner join deleted as d
				on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.estado<>d.estado
			if @@error!=0 begin
				raiserror ('Error actualizando el estado', 11, 1)
				rollback
			end	

	-- David SAnchez... 26/06/06
	-- Todos los que el estado se ponga en dos se pone vendedor NV

	update clientes set vendedor='NV' FROM inserted as i inner join clientes as c on i.empresa=c.empresa and i.[nº cliente]=c.[nº cliente] and i.contacto=c.contacto
		where i.estado=2
		if @@error!=0 begin
				raiserror ('Error actualizando el estado', 11, 1)
				rollback
		end	
	
/*	
	declare @EmpresaEstado as char(3)
	declare @CliEstado as char (10)
	declare @ContactoEstado as char(3)
	declare @EstadoAnt as char(3)
	declare @EstadoNue as char(3)	
	
	set @CliEstado=(select [nº cliente] from inserted)
	set @EmpresaEstado=(select empresa from inserted)
	set @ContactoEstado=(select Contacto from inserted)
	
	-- buscamos el estado antiguo
	set @EstadoAnt =(select estado from deleted)
	-- buscamos el estado nuevo
	set @EstadoNue=(select estado from inserted)
	
	-- si son distintos actualizamos
	if @EstadoAnt is null and @EstadoNue is not null begin
			-- insertamos en la tabla log modificaciones
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			values (@EmpresaEstado,'Clientes','Actualizar',@CliEstado,'Estado',getdate(),@EstadoAnt)
			if @@error!=0 begin
				raiserror ('Error actualizando el estado', 11, 1)
				rollback
			end	
	end else  if @EstadoAnt is not null and @EstadoNue is  null begin
		-- insertamos en la tabla log modificaciones
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			values (@EmpresaEstado,'Clientes','Actualizar',@CliEstado,'Estado',getdate(),@EstadoAnt)
			if @@error!=0 begin
				raiserror ('Error actualizando el estado', 11, 1)
				rollback
			end	
	end else  if @EstadoAnt<> @EstadoNue begin
		-- insertamos en la tabla log modificaciones
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			values (@EmpresaEstado,'Clientes','Actualizar',@CliEstado,'Estado',getdate(),@EstadoAnt)
			if @@error!=0 begin
				raiserror ('Error actualizando el estado', 11, 1)
				rollback
			end	
	end
*/
end
if update(periodoFacturacion) begin
	declare @ContadoRiguroso as char(15)
	declare @FormaPagoAlbaran as char(3)
	declare @Usuario as char(50)
	declare @Cliente as char(15)
	declare @Contacto as char(3)
	declare @SentenciaError as char(100)
	set @contadoRiguroso=''
	set @FormaPagoAlbaran=''
	set @Usuario = stuff(system_user,1,patindex('%\%',system_user),'')
	-- Buscamos cual es el plazo de pago de contado riguroso
	set @ContadoRiguroso=(select top 1 valor from parametrosusuario  with (nolock) where usuario=@usuario  and clave='NumeroContadoRiguroso' order by empresa)
	if ltrim(rtrim(@contadoriguroso))='' or  @contadoRiguroso=null begin
		set @ContadoRiguroso=(select top 1 valor from parametrosusuario  with (nolock) where usuario='(defecto)'  and clave='NumeroContadoRiguroso' order by empresa)
	end
	-- Buscamos la forma de pago del albaran
	set @FormaPagoAlbaran=(select top 1 valor from parametrosusuario  with (nolock) where usuario=@usuario  and clave='periodofacturaciónalbarán' order by empresa)
	if ltrim(rtrim(@FormaPagoAlbaran))='' or  @FormaPagoAlbaran=null begin
		set @FormaPagoAlbaran=(select top 1 valor from parametrosusuario  with (nolock) where usuario='(defecto)'  and clave='periodofacturaciónalbarán' order by empresa)
	end
	if (
		select count(*) from inserted  as c  inner join condpagoclientes as i with (nolock)
			on c.empresa=i.empresa and c.[nº cliente]=i.[nº cliente] and c.contacto=i.contacto
				where c.periodofacturacion=@formapagoalbaran and i.plazospago=@contadoriguroso
		)>0 begin
			select top 1 @Cliente=  c.[nº cliente],@contacto=c.contacto from inserted as c inner join condpagoclientes as i with (nolock)
				on c.empresa=i.empresa and c.[nº cliente]=i.[nº cliente] and c.contacto=i.contacto
				where c.periodofacturacion=@formapagoalbaran and i.plazospago=@contadoriguroso and c.[nº cliente]<>0
				
				set @sentenciaError= 'El cliente '+ltrim(rtrim(@cliente))+' contacto '+ltrim(rtrim(@contacto))+' no puede ser '+ltrim(rtrim(@FormaPagoAlbaran))+' y ' +ltrim(rtrim(@ContadoRiguroso))
				set @SentenciaError=ltrim(rtrim(@SentenciaError))
				raiserror(@sentenciaError,11,1)
				rollback
	end
end


-- David Sanchez 13/01/06... poblacion codigo postal y vendedor

 if update(Población) begin
	-- David Sanchez 30/06/04
	-- Ponemos el insert con una select porque si no no se podia actualizar varios a la vez.
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			select i.empresa,'Clientes','Actualizar',i.[nº cliente],'Poblacion',getdate(),d.poblacion
				from inserted as i inner join deleted as d
				on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.poblacion<>d.poblacion
			if @@error!=0 begin
				raiserror ('Error actualizando la población', 11, 1)
				rollback
			end	
end

if update(CodPostal) begin
	-- David Sanchez 30/06/04
	-- Ponemos el insert con una select porque si no no se podia actualizar varios a la vez.
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			select i.empresa,'Clientes','Actualizar',i.[nº cliente],'CodPostal',getdate(),d.Codpostal
				from inserted as i inner join deleted as d
				on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.CodPostal<>d.CodPostal
			if @@error!=0 begin
				raiserror ('Error actualizando codigo postal', 11, 1)
				rollback
			end	

	-- Carlos Adrián Martínez: 20/02/17. Actualizamos el vendedor de peluquería
	declare @clientes as table (
	empresa char(3),
	cliente char(10),
	contacto char(3),
	codPostalAnterior char(15),
	codPostalNuevo char(15)
)

	insert into @clientes select i.Empresa, i.[Nº Cliente], i.Contacto, d.CodPostal, i.CodPostal
	from inserted as i inner join deleted as d
				on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.CodPostal<>d.CodPostal

	if (select count(*) from @clientes) > 0 begin
		delete VendedoresClienteGrupoProducto where Id in (
			select c2.Id  from
				(select v.Id, c.*
				from @clientes as c inner join VendedoresCodigoPostalGrupoProducto as v
				on c.empresa = v.empresa and c.codPostalNuevo = v.codigopostal) as c1
			right join 
				(select v2.* 
				from VendedoresClienteGrupoProducto as v2 inner join @clientes as c
				on c.empresa = v2.empresa and c.cliente = v2.cliente and c.contacto = v2.contacto) as c2
			on c1.empresa = c2.empresa and c1.cliente = c2.cliente and c1.contacto = c2.contacto
			where c1.id is null and c2.id is not null
		)
	
		if @@ROWCOUNT = 0 begin
			update VendedoresClienteGrupoProducto set vendedor = v.vendedor
			from @clientes as c inner join VendedoresCodigoPostalGrupoProducto as v
			--on c.empresa = v.empresa and c.codPostalNuevo = v.codigopostal
			on c.empresa in ('1','3') and v.empresa = '1' and c.codPostalNuevo = v.codigopostal
			left join VendedoresClienteGrupoProducto as v2
			on c.empresa = v2.empresa and c.cliente = v2.cliente and c.contacto = v2.contacto
			where v.id is not null and v2.id is not null and v.vendedor <> v2.vendedor

			if @@ROWCOUNT = 0 begin
				insert VendedoresClienteGrupoProducto (Empresa, Cliente, Contacto, GrupoProducto, Vendedor)
				select c.empresa, c.cliente, c.contacto, v.grupoproducto, v.vendedor
				from @clientes as c inner join VendedoresCodigoPostalGrupoProducto as v
				--on c.empresa = v.empresa and c.codPostalNuevo = v.codigopostal
				on c.empresa in ('1','3') and v.empresa = '1' and c.codPostalNuevo = v.codigopostal
				left join VendedoresClienteGrupoProducto as v2
				on c.empresa = v2.empresa and c.cliente = v2.cliente and c.contacto = v2.contacto
				where v.id is not null and v2.id is null
			end
		end
	end

end

if update(Vendedor) begin
	-- David Sanchez 30/06/04
	-- Ponemos el insert con una select porque si no no se podia actualizar varios a la vez.
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			select i.empresa,'Clientes','Actualizar',i.[nº cliente],'Vendedor',getdate(),d.Vendedor
				from inserted as i inner join deleted as d
				on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and isnull(i.Vendedor,'')<>isnull(d.Vendedor,'')
			if @@error!=0 begin
				raiserror ('Error actualizando el vendedor', 11, 1)
				rollback
			end	
	-- Carlos 04/04/12: mandamos el correo con los cambios

	-- Sólo enviamos si no es el propio vendedor quién pasa su cliente a NV.
	if ((
	SELECT count(*) from inserted as i inner join deleted as d
	on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and isnull(i.Vendedor,'')<>isnull(d.Vendedor,'')
	left join usuariovendedor as u
	on d.vendedor = u.vendedor
	--where stuff(system_user,1,patindex('%\%',system_user),'') <> u.usuario or i.vendedor <> 'NV'
	) > 0) or (not EXISTS(SELECT 1 FROM DELETED)) begin



		declare @destinatarios as varchar(1000) = ''
		select @destinatarios = @destinatarios + rtrim(mail) + '; ' from 
		(select distinct(mail) from vendedores where empresa = '1' and estado >= 0 and mail is not null 
		and mail <> 'almudenagonzalez@nuevavision.es'
		and número in
		(select Vendedor from deleted where empresa = '1' --and [fecha modificación] < dateadd(minute, -10, getdate())
		union
		select vendedor from inserted where empresa = '1' --and [fecha modificación] < dateadd(minute, -10, getdate())
		)) as a 
	
		-- Verificar si alguno de los vendedores pertenece al equipo de ASH
		IF EXISTS (
			SELECT 1 
			FROM (
				SELECT Vendedor FROM deleted WHERE empresa = '1'
				UNION
				SELECT Vendedor FROM inserted WHERE empresa = '1'
			) AS vendedores
			WHERE Vendedor IN (SELECT Vendedor FROM EquiposVenta WHERE Superior = 'ASH')
		)
		BEGIN
			SET @destinatarios = RTRIM(@destinatarios) + '; albertosancho@nuevavision.es'
		END

		DECLARE @tableHTML  NVARCHAR(MAX) ;
	
		if @destinatarios is not null and rtrim(@destinatarios) <> '' begin
			SET @tableHTML =
			N'<H1>Informe de Cambios de Vendedores</H1>' +
			N'<table border="1">' +
			N'<tr><th>Cliente</th><th>Contacto</th><th>Dirección</th><th>C.P.</th><th>Vendedor Anterior</th>' +
			N'<th>Vendedor Nuevo</th>' +
			N'<th>Estado</th>' +
			N'<th>Usuario en ficha</th>' +
			N'<th>Usuario que hace el cambio</th></tr>' +
			CAST ( ( SELECT td = i.[nº cliente],       '',
							td = i.contacto, '',
							td = isnull(i.dirección,''), '',
							td = isnull(i.CodPostal,''), '',
							td = isnull(d.vendedor,'S/V'), '',
							td = isnull(i.vendedor,''), '',
							td = isnull(i.Estado,''), '',
							td = isnull(i.Usuario,''), '',
							td = system_user
  						from inserted as i left join deleted as d
						on (i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.Vendedor<>d.Vendedor) or (d.Empresa is null)
	              
					FOR XML PATH('tr'), TYPE 
			) AS NVARCHAR(MAX) ) +
			N'</table>' ;
	
		
		
			EXEC msdb.dbo.sp_send_dbmail
			@profile_name = 'Nesto',
			@recipients = @Destinatarios,
			@copy_recipients = 'carlosadrian@nuevavision.es; manuelrodriguez@nuevavision.es',
			@body = @tableHTML,
			@body_format = 'HTML',
			@subject = 'Cambio de Vendedor' ;
		end
	end
end

/*
if system_user = 'NUEVAVISION\Carlos' begin
select empresa, [nº cliente], contacto, direccion from inserted
select empresa, [nº cliente], contacto, direccion from deleted
/*
select i.empresa,'Clientes','Actualizar',i.[nº cliente],'Nombre',getdate(),d.Nombre, i.nombre
	from inserted as i inner join deleted as d
	on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto 
	where i.Nombre<>d.Nombre
*/			
select i.empresa,i.[nº cliente],i.contacto,d.Dirección
from inserted as i inner join deleted as d
on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.Dirección<>d.Dirección
end
*/

if update(Nombre) begin
	-- Carlos Adrián Martínez 03/01/11
	-- Ponemos el insert con una select porque si no no se podia actualizar varios a la vez.
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			select i.empresa,'Clientes','Actualizar',i.[nº cliente],'Nombre',getdate(),d.Nombre
				from inserted as i inner join deleted as d
				on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.Nombre<>d.Nombre
			if @@error!=0 begin
				raiserror ('Error actualizando el Nombre', 11, 1)
				rollback
			end	
end

if update(Dirección) begin
	-- Carlos Adrián Martínez 03/01/11
	-- Ponemos el insert con una select porque si no no se podia actualizar varios a la vez.
		insert into logmodificaciones (Empresa,Tabla,procedimiento, Numero,Columna, Fecha,ValorAntiguo)
			select i.empresa,'Clientes','Actualizar',i.[nº cliente],'Dirección',getdate(),d.Dirección
				from inserted as i inner join deleted as d
				on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.Dirección<>d.Dirección
			if @@error!=0 begin
				raiserror ('Error actualizando el Dirección', 11, 1)
				rollback
			end	
			
	-- Carlos 11/04/12. Mandamos un correo si se deja la dirección a NULL
	if (
		select count(*) from deleted as d inner join inserted as i 
		on d.empresa = i.empresa and d.[nº cliente] = i.[nº cliente] and d.contacto = i.contacto
		where d.direccion is not null and (i.direccion is null or ltrim(i.dirección)='')) > 0 begin

		declare @DireccionMal as char(10) = (select top 1 d.[nº cliente] from deleted as d inner join inserted as i 
		on d.empresa = i.empresa and d.[nº cliente] = i.[nº cliente] and d.contacto = i.contacto
		where d.direccion is not null and (i.direccion is null or ltrim(i.dirección)=''))		
		raiserror ('Lo que el usuario intenta hacer borraría la dirección del cliente %s', 11, 1, @DireccionMal)
		rollback		
	end

	-- Carlos 21/03/19: mandamos correo si cambia la dirección
	if ((
	SELECT count(*) from inserted as i inner join deleted as d
	on i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and isnull(i.Dirección,'')<>isnull(d.Dirección,'') and d.Dirección is not null
	left join usuariovendedor as u
	on d.vendedor = u.vendedor
	--where stuff(system_user,1,patindex('%\%',system_user),'') <> u.usuario or i.vendedor <> 'NV'
	) > 0) begin



		declare @destinatariosDireccion as varchar(1000) = 'carlosadrian@nuevavision.es';
		/*
		select @destinatariosDireccion = @destinatariosDireccion + rtrim(mail) + '; ' from 
		(select distinct(mail) from vendedores where empresa = '1' and estado >= 0 and mail is not null and número in
		(select Vendedor from deleted where empresa = '1' --and [fecha modificación] < dateadd(minute, -10, getdate())
		union
		select vendedor from inserted where empresa = '1' --and [fecha modificación] < dateadd(minute, -10, getdate())
		)) as a 
		*/
	
		DECLARE @tableHTML_Direccion  NVARCHAR(MAX) ;
	
		if @destinatariosDireccion is not null and rtrim(@destinatariosDireccion) <> '' begin
			SET @tableHTML_Direccion =
			N'<H1>Informe de Cambios de Dirección</H1>' +
			N'<table border="1">' +
			N'<tr><th>Cliente</th><th>Contacto</th><th>Vendedor</th><th>País</th><th>C.P.</th><th>Dirección Anterior</th>' +
			N'<th>Dirección Nueva</th>' +
			N'<th>Estado</th>' +
			N'<th>Usuario en ficha</th>' +
			N'<th>Usuario que hace el cambio</th></tr>' +
			CAST ( ( SELECT td = i.[nº cliente],       '',
							td = i.contacto, '',
							td = isnull(i.Vendedor,''), '',
							td = isnull(i.Pais,''), '',
							td = isnull(i.CodPostal,''), '',
							td = isnull(d.Dirección,'Sin Dirección'), '',
							td = isnull(i.Dirección,''), '',
							td = isnull(i.Estado,''), '',
							td = isnull(i.Usuario,''), '',
							td = system_user
  						from inserted as i left join deleted as d
						on (i.empresa=d.empresa and i.[nº cliente]=d.[nº cliente] and i.contacto=d.contacto and i.Dirección<>d.Dirección) or (d.Empresa is null)
	              
					FOR XML PATH('tr'), TYPE 
			) AS NVARCHAR(MAX) ) +
			N'</table>' ;
	
		
		
			EXEC msdb.dbo.sp_send_dbmail
			@profile_name = 'Nesto',
			@recipients = @destinatariosDireccion,
			--@copy_recipients = 'direccion@nuevavision.es',
			@body = @tableHTML_Direccion,
			@body_format = 'HTML',
			@subject = 'Cambio de Dirección' ;
		end
	end
end


IF (SYSTEM_USER != 'NUEVAVISION\RDS2016$')
   -- Carlos/Claude 27/10/26 (Issue716): solo si alguna de las columnas que se sincronizan va en el SET. Un UPDATE masivo
   -- de otras columnas (p. ej. el backfill de FechaAlta, 55.000 fichas) entraba aquí y el IF EXISTS de abajo, con
   -- inserted y deleted sin índice, hacía un bucle anidado de 55.000 x 55.000 (más de 30 minutos con Clientes bloqueada).
   AND (UPDATE([CIF/NIF]) OR UPDATE([Nº Cliente]) OR UPDATE(Contacto) OR UPDATE(ClientePrincipal) OR UPDATE(Nombre)
        OR UPDATE(Dirección) OR UPDATE(CodPostal) OR UPDATE(Población) OR UPDATE(Provincia) OR UPDATE(Teléfono)
        OR UPDATE(Vendedor) OR UPDATE(Estado))
BEGIN
    -- Verificar si algún campo ha cambiado (ignorando espacios en blanco al final o al inicio)
	-- (variable + SELECT TOP 1 en vez de IF EXISTS: OPTION no se admite dentro de una subconsulta)
	DECLARE @hayCambiosSync bit = 0;
	SELECT TOP (1) @hayCambiosSync = 1
		FROM inserted i
		JOIN deleted d ON i.Empresa = d.Empresa AND i.[Nº Cliente] = d.[Nº Cliente] AND i.Contacto = d.Contacto
		WHERE 
			ISNULL(LTRIM(RTRIM(i.[CIF/NIF])), '') <> ISNULL(LTRIM(RTRIM(d.[CIF/NIF])), '') OR
			ISNULL(i.[Nº Cliente], '') <> ISNULL(d.[Nº Cliente], '') OR
			ISNULL(i.Contacto, '') <> ISNULL(d.Contacto, '') OR
			ISNULL(LTRIM(RTRIM(i.ClientePrincipal)), '') <> ISNULL(LTRIM(RTRIM(d.ClientePrincipal)), '') OR
			ISNULL(LTRIM(RTRIM(i.Nombre)), '') <> ISNULL(LTRIM(RTRIM(d.Nombre)), '') OR
			ISNULL(LTRIM(RTRIM(i.Dirección)), '') <> ISNULL(LTRIM(RTRIM(d.Dirección)), '') OR
			ISNULL(LTRIM(RTRIM(i.CodPostal)), '') <> ISNULL(LTRIM(RTRIM(d.CodPostal)), '') OR
			ISNULL(LTRIM(RTRIM(i.Población)), '') <> ISNULL(LTRIM(RTRIM(d.Población)), '') OR
			ISNULL(LTRIM(RTRIM(i.Provincia)), '') <> ISNULL(LTRIM(RTRIM(d.Provincia)), '') OR
			ISNULL(LTRIM(RTRIM(i.Teléfono)), '') <> ISNULL(LTRIM(RTRIM(d.Teléfono)), '') OR
			ISNULL(LTRIM(RTRIM(i.Vendedor)), '') <> ISNULL(LTRIM(RTRIM(d.Vendedor)), '') OR
			ISNULL(LTRIM(RTRIM(i.Estado)), '') <> ISNULL(LTRIM(RTRIM(d.Estado)), '') OR
			-- Casos explícitos para detectar cambios de NULL a valor o viceversa
			(i.[CIF/NIF] IS NULL AND d.[CIF/NIF] IS NOT NULL) OR
			(i.[CIF/NIF] IS NOT NULL AND d.[CIF/NIF] IS NULL) OR
			(i.[Nº Cliente] IS NULL AND d.[Nº Cliente] IS NOT NULL) OR
			(i.[Nº Cliente] IS NOT NULL AND d.[Nº Cliente] IS NULL) OR
			(i.Contacto IS NULL AND d.Contacto IS NOT NULL) OR
			(i.Contacto IS NOT NULL AND d.Contacto IS NULL) OR
			(i.ClientePrincipal IS NULL AND d.ClientePrincipal IS NOT NULL) OR
			(i.ClientePrincipal IS NOT NULL AND d.ClientePrincipal IS NULL) OR
			(i.Nombre IS NULL AND d.Nombre IS NOT NULL) OR
			(i.Nombre IS NOT NULL AND d.Nombre IS NULL) OR
			(i.Dirección IS NULL AND d.Dirección IS NOT NULL) OR
			(i.Dirección IS NOT NULL AND d.Dirección IS NULL) OR
			(i.CodPostal IS NULL AND d.CodPostal IS NOT NULL) OR 
			(i.CodPostal IS NOT NULL AND d.CodPostal IS NULL) OR
			(i.Población IS NULL AND d.Población IS NOT NULL) OR
			(i.Población IS NOT NULL AND d.Población IS NULL) OR
			(i.Provincia IS NULL AND d.Provincia IS NOT NULL) OR
			(i.Provincia IS NOT NULL AND d.Provincia IS NULL) OR
			(i.Teléfono IS NULL AND d.Teléfono IS NOT NULL) OR
			(i.Teléfono IS NOT NULL AND d.Teléfono IS NULL) OR
			(i.Vendedor IS NULL AND d.Vendedor IS NOT NULL) OR
			(i.Vendedor IS NOT NULL AND d.Vendedor IS NULL) OR
			(i.Estado IS NULL AND d.Estado IS NOT NULL) OR
			(i.Estado IS NOT NULL AND d.Estado IS NULL)
		OPTION (HASH JOIN); -- 27/10/26: inserted/deleted no tienen índice; con muchas filas el bucle anidado es cuadrático
	IF @hayCambiosSync = 1
	BEGIN
		-- Si hay cambios, insertar en la tabla de sincronización
		INSERT INTO Nesto_sync (Tabla, ModificadoId) 
		SELECT 'Clientes', i.[Nº Cliente]
		FROM inserted i
		WHERE i.Empresa = '1'
		GROUP BY i.[Nº Cliente];
	END
END
GO
