/*
    NestoAPI#727 (28/10/26): factura NV2616046 (cliente 27593) con pedidos de julio sin P.O. y de septiembre con dos P.O.
    distintos, y el pedido agrupado sin P.O. Cambios en prdAgruparAlbaranesVta (la agrupación por cliente+contacto que
    lanza el fin de mes):
      1. La comprobación de P.O. distintos (Issue #58) cuenta también los pedidos SIN P.O.: ya no se mezclan unos con otros.
      2. La agrupación usa la misma fecha que la comprobación y el cursor (hasta @FechaAlbarán incluido): «<= @FechaAlbarán + 1»
         metía los albaranes del día siguiente sin pasar por la comprobación.
      3. Clientes con P.O. obligatorio (NestoAPI#723, ClientesSuPedidoObligatorio): ningún albarán sin P.O. se agrupa; avisa cuál.
      4. El pedido agrupado hereda el P.O. (SuPedido) de los pedidos que agrupa.
    OJO: la reunión posterior de TODOS los contactos de un cliente en un único pedido nuevo (el 927473, contacto 0) NO la hace
    este procedimiento: la hace el Nesto viejo (VB6) después, y ahí no hay comprobación de P.O. Hasta que ese paso pase a la
    API (NestoAPI#727), con un cliente marcado como P.O. obligatorio la API no deja facturar ese pedido sin P.O.
    Idempotente (ALTER). Lanzar como sa en NV. Sin GRANTs nuevos: el procedimiento ya existe con sus permisos.
*/
SET NOCOUNT ON;
USE NV;
GO
ALTER PROCEDURE [dbo].[prdAgruparAlbaranesVta] @Empresa as char(3), @FechaAlbarán as datetime, @PeriodoFacturación as char(3), @NumCliente as char(10), @Contacto as char(3)  AS
-- Agrupa en un sólo pedido todos los albaranes de un determinado cliente, hasta una fecha determinada. También filtra por PeriodoFacturación
-- Si se pasa cadena vacía en el parámetro @NumCliente lo hace para todos los clientes
-- Carlos Adrián Martínez -- 17/07/02
-- Comprobación de parámetros
if ((@NumCliente='') or (@Contacto = '')) and (@NumCliente<>@Contacto) begin
	raiserror ('Debe especificar Cliente y Contacto o ninguno de los dos',11,1)
	return -1
end

-- Issue #58: Validar que no haya POs diferentes para el mismo cliente/contacto
IF @NumCliente = ''
BEGIN
    -- Validar para todos los clientes
    IF EXISTS (
        SELECT v.[Nº Cliente], v.Contacto
        FROM vstAgruparAlbaranesVta v
        INNER JOIN CabPedidoVta c ON v.Empresa = c.Empresa AND v.NºPedido = c.Número
        WHERE v.Empresa = @Empresa
          AND v.fecha < @FechaAlbarán + 1
          AND v.PeriodoFacturación = @PeriodoFacturación
        GROUP BY v.[Nº Cliente], v.Contacto
        HAVING COUNT(DISTINCT ISNULL(RTRIM(c.SuPedido), '')) > 1
    )
    BEGIN
        DECLARE @clienteConflicto char(10), @contactoConflicto char(3)
        SELECT TOP 1 @clienteConflicto = v.[Nº Cliente], @contactoConflicto = v.Contacto
        FROM vstAgruparAlbaranesVta v
        INNER JOIN CabPedidoVta c ON v.Empresa = c.Empresa AND v.NºPedido = c.Número
        WHERE v.Empresa = @Empresa
          AND v.fecha < @FechaAlbarán + 1
          AND v.PeriodoFacturación = @PeriodoFacturación
        GROUP BY v.[Nº Cliente], v.Contacto
        HAVING COUNT(DISTINCT ISNULL(RTRIM(c.SuPedido), '')) > 1

        RAISERROR ('No se pueden agrupar albaranes del cliente %s (%s) porque tienen diferentes números de pedido del cliente (P.O.), o unos lo tienen y otros no. Agrupe y facture cada P.O. por separado.', 11, 1, @clienteConflicto, @contactoConflicto)
        RETURN -1
    END
END
ELSE
BEGIN
    -- Validar para el cliente específico
    IF EXISTS (
        SELECT 1
        FROM vstAgruparAlbaranesVta v
        INNER JOIN CabPedidoVta c ON v.Empresa = c.Empresa AND v.NºPedido = c.Número
        WHERE v.Empresa = @Empresa
          AND v.fecha < @FechaAlbarán + 1
          AND v.PeriodoFacturación = @PeriodoFacturación
          AND v.[Nº Cliente] = @NumCliente
          AND v.Contacto = @Contacto
        GROUP BY v.[Nº Cliente], v.Contacto
        HAVING COUNT(DISTINCT ISNULL(RTRIM(c.SuPedido), '')) > 1
    )
    BEGIN
        RAISERROR ('No se pueden agrupar albaranes del cliente %s (%s) porque tienen diferentes números de pedido del cliente (P.O.), o unos lo tienen y otros no. Agrupe y facture cada P.O. por separado.', 11, 1, @NumCliente, @Contacto)
        RETURN -1
    END
END

-- NestoAPI#727 (28/10/26): los clientes que exigen el P.O. en todos sus pedidos (NestoAPI#723, tabla ClientesSuPedidoObligatorio)
-- no pueden agrupar albaranes de pedidos sin P.O.: se para aquí y se dice cuál es el primero.
IF OBJECT_ID('dbo.ClientesSuPedidoObligatorio', 'U') IS NOT NULL
BEGIN
    DECLARE @pedidoSinPO int, @clienteSinPO char(10)
    SELECT TOP 1 @pedidoSinPO = c.Número, @clienteSinPO = c.[Nº Cliente]
    FROM vstAgruparAlbaranesVta v
    INNER JOIN CabPedidoVta c ON v.Empresa = c.Empresa AND v.NºPedido = c.Número
    INNER JOIN ClientesSuPedidoObligatorio o ON o.Empresa = c.Empresa AND o.Cliente = c.[Nº Cliente]
    WHERE v.Empresa = @Empresa
      AND v.fecha < @FechaAlbarán + 1
      AND v.PeriodoFacturación = @PeriodoFacturación
      AND (@NumCliente = '' OR (v.[Nº Cliente] = @NumCliente AND v.Contacto = @Contacto))
      AND ISNULL(RTRIM(c.SuPedido), '') = ''
    ORDER BY c.Número
    IF @pedidoSinPO IS NOT NULL
    BEGIN
        RAISERROR ('El cliente %s exige el número de pedido (P.O.) y el pedido %d no lo tiene: póngaselo antes de agrupar (o no se agrupa ni se factura).', 11, 1, @clienteSinPO, @pedidoSinPO)
        RETURN -1
    END
END

-- Declaración de variables
declare @Exito as bit -- para saber al final si tengo que hacer commit o rollback
--set @FechaAlbarán = dateadd(ms,-1,@fechaalbarán+1)
--print @fechaalbarán+1
if @NumCliente = '' -- Si me pasan el parámetro @NumCliente lo hago sólo para ese cliente, sino para todos
	declare crsCabVta cursor local fast_forward for select [nº cliente],contacto,min(NºPedido) as NºPedido from vstAgruparAlbaranesVta where Empresa = @Empresa and fecha < @FechaAlbarán+1 and PeriodoFacturación = @PeriodoFacturación group by [nº cliente],contacto order by [Nº Cliente]
else
	declare crsCabVta cursor local fast_forward for select [nº cliente],contacto,min(NºPedido) as NºPedido from vstAgruparAlbaranesVta where Empresa = @Empresa and fecha < @FechaAlbarán+1 and PeriodoFacturación = @PeriodoFacturación and [Nº cliente] = @NumCliente and Contacto = @Contacto group by [nº cliente],contacto
declare @NumPedido as int -- coge el último pedido de cada cliente, que es en el que se agruparán el resto
-- Inicialización de variables
set @Exito = 1 
-- COMIENZO TRANSACCIÓN -----------------------------------------------------------------------------------------------------------------------------------
begin transaction
	/*
	update pedidosespeciales set nºpedidovta=null  where  empresa=@empresa --where nºpedidovta in (select NºPedido from VSTAGRUPARALBARANESVTA where Empresa = @Empresa and fecha < @FechaAlbarán + 1 and PeriodoFacturación = @PeriodoFacturación and [Nº Cliente] = @NumCliente and Contacto = @Contacto)
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	*/
	/*
	-- tenemos tambien que cambiar cambiar el pedido de la ubicacion, por lo que lo dejamos a nulo
	update ubicaciones set pedidovta=null where  empresa=@empresa--where pedidovta in (select NºPedido from VSTAGRUPARALBARANESVTA where Empresa = @Empresa and fecha < @FechaAlbarán + 1 and PeriodoFacturación = @PeriodoFacturación and [Nº Cliente] = @NumCliente and Contacto = @Contacto)
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	*/
-- 1.- Recorrer LinPedidoVta en estado 2. Entre estas líneas separar las que tengan una cabecera con PeriodoFacturación = FDM. Quizá lo más cómodo para hacer esto sea crear una vista (por ejemplo vstFinDeMes).
-- 2.- Buscar la última cab pedidovta de ese cliente para añadirla todas las lineas
-- 3.- Hacer un update a las lineas que corresponden a ese cliente/contacto cambiandolas el número por el de la nueva cabecera.
open crsCabVta
fetch next from crsCabVta into @NumCliente,@Contacto,@NumPedido
while (@@fetch_status = 0) and (@Exito = 1) begin -- mientras haya clientes y no se haya producido ningún error
	-- modificacamos el pedido especial
	update pedidosespeciales set nºpedidovta=null  where  empresa=@empresa and  nºpedidovta in (select NºPedido from VSTAGRUPARALBARANESVTA where Empresa = @Empresa and fecha < @FechaAlbarán + 1 and PeriodoFacturación = @PeriodoFacturación and [Nº Cliente] = @NumCliente and Contacto = @Contacto)
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	
	/*-- Carlos 31/05/17: anulo por tiempo de espera
	-- tenemos tambien que cambiar cambiar el pedido de la ubicacion, por lo que lo dejamos a nulo
	update ubicaciones set pedidovta=null where  empresa=@empresa and  pedidovta in (select NºPedido from VSTAGRUPARALBARANESVTA where Empresa = @Empresa and fecha < @FechaAlbarán + 1 and PeriodoFacturación = @PeriodoFacturación and [Nº Cliente] = @NumCliente and Contacto = @Contacto)
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	*/
	update CabPedidoVta set Agrupada = 1,fecha=@FechaAlbarán  where Empresa = @Empresa and Número = @NumPedido
	-- NestoAPI#727: el pedido agrupado se queda con el P.O. de los pedidos que agrupa (es único: ya se ha comprobado arriba).
	update CabPedidoVta set SuPedido = (
			select top 1 c.SuPedido from vstAgruparAlbaranesVta v inner join CabPedidoVta c on v.Empresa = c.Empresa and v.NºPedido = c.Número
			where v.Empresa = @Empresa and v.fecha < @FechaAlbarán + 1 and v.PeriodoFacturación = @PeriodoFacturación
			  and v.[Nº Cliente] = @NumCliente and v.Contacto = @Contacto and ISNULL(RTRIM(c.SuPedido), '') <> '')
		where Empresa = @Empresa and Número = @NumPedido and ISNULL(RTRIM(SuPedido), '') = ''
	if @@error != 0 begin
		raiserror ('No se pudo copiar el P.O. al pedido agrupado del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	update vstAgruparAlbaranesVta set NºPedido = @NumPedido where Empresa = @Empresa and fecha < @FechaAlbarán + 1 and PeriodoFacturación = @PeriodoFacturación and [Nº Cliente] = @NumCliente and Contacto = @Contacto
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	-- volvemos a poner el pedido especial
	update pedidosespeciales set nºpedidovta=l.numero from linpedidovta as l inner join pedidosespeciales as p on l.[nº orden]=p.nºordenvta where p.nºpedidovta is null and p.empresa=@empresa and l.[nº cliente]=@NumCliente and contacto=@contacto
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	update ubicaciones set pedidovta=l.numero from linpedidovta as l inner join ubicaciones as u on l.[nº orden]=u.NºOrdenvta where u.pedidovta is null and u.empresa=@empresa and l.[nº cliente]=@numcliente and contacto=@contacto
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	--update pedidosespeciales set nºpedidovta=@numpedido where nºpedidovta is null and nºcliente=@numcliente and contactocliente=@contacto and NºOrdenVta is not null
	fetch next from crsCabVta into @NumCliente,@Contacto,@NumPedido
end
	/*
	update pedidosespeciales set nºpedidovta=l.numero from linpedidovta as l inner join pedidosespeciales as p on l.[nº orden]=p.nºordenvta where p.nºpedidovta is null and p.empresa=@empresa
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	*/
	/*
	-- a continuacion ponemos el numero de pedido de todos los que tengan el numero de orden vacio
	update ubicaciones set pedidovta=l.numero from linpedidovta as l inner join ubicaciones as u on l.[nº orden]=u.NºOrdenvta where u.pedidovta is null and u.empresa=@empresa
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
		set @Exito = 0
	end
	*/
-- 4.- Llamar a prdCrearFacturaVta con ese nuevo pedido.
----> De momento no se va a hacer, prefieren revisar los pedidos
-- 5.- Borrar las cabeceras que hemos dejado sin líneas
-- Carlos 27/11/14: lo comento para que no dé error con los EnvíosAgencia
/*
if @Exito = 1 begin
	delete CabPedidoVta from CabPedidoVta inner join vstCabPedidoVtaSinLineas on CabPedidoVta.Número = vstCabPedidoVtaSinLineas.Número where CabPedidoVta.empresa = @Empresa and vstCabPedidoVtaSinLineas.Empresa = @Empresa
	if @@error != 0 begin
		raiserror ('No se pudieron actualizar las cabeceras sin líneas',11,1)
		set @Exito = 0
	end
end
*/
-- 6.- Sacar un grid con las cabeceras que quedan con lineas en estado 2, para comprobar que ninguna de ellas son de FDM.
-----> El punto 6 se hace en Visual Basic
-- Finalizamos
if @Exito=1
	commit transaction
else
	rollback transaction
close crsCabVta
deallocate crsCabVta
-- FIN DE LA TRANSACCIÓN ----------------------------------------------------------------------------------------------------------------------------
GO
