-- Issue #58: Añadir campo SuPedido (Purchase Order / P.O.) en pedidos y facturas
-- Ejecutar ANTES de desplegar el código

-- 1. Añadir columna a CabPedidoVta
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabPedidoVta' AND COLUMN_NAME = 'SuPedido')
BEGIN
    ALTER TABLE CabPedidoVta ADD SuPedido nvarchar(50) NULL;
    PRINT 'Columna SuPedido añadida a CabPedidoVta';
END
ELSE
    PRINT 'Columna SuPedido ya existe en CabPedidoVta';
GO

-- 2. Añadir columna a CabFacturaVta
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'SuPedido')
BEGIN
    ALTER TABLE CabFacturaVta ADD SuPedido nvarchar(50) NULL;
    PRINT 'Columna SuPedido añadida a CabFacturaVta';
END
ELSE
    PRINT 'Columna SuPedido ya existe en CabFacturaVta';
GO

-- 3. Modificar prdAgruparAlbaranesVta para impedir agrupar albaranes con POs diferentes
ALTER PROCEDURE [dbo].[prdAgruparAlbaranesVta]
    @Empresa as char(3),
    @FechaAlbarán as datetime,
    @PeriodoFacturación as char(3),
    @NumCliente as char(10),
    @Contacto as char(3)
AS
-- Agrupa en un sólo pedido todos los albaranes de un determinado cliente, hasta una fecha determinada.
-- También filtra por PeriodoFacturación
-- Si se pasa cadena vacía en el parámetro @NumCliente lo hace para todos los clientes
-- Carlos Adrián Martínez -- 17/07/02
-- Issue #58: Validar que no se agrupen albaranes con POs diferentes -- 12/03/26

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
          AND ISNULL(c.SuPedido, '') <> ''
        GROUP BY v.[Nº Cliente], v.Contacto
        HAVING COUNT(DISTINCT c.SuPedido) > 1
    )
    BEGIN
        DECLARE @clienteConflicto char(10), @contactoConflicto char(3)
        SELECT TOP 1 @clienteConflicto = v.[Nº Cliente], @contactoConflicto = v.Contacto
        FROM vstAgruparAlbaranesVta v
        INNER JOIN CabPedidoVta c ON v.Empresa = c.Empresa AND v.NºPedido = c.Número
        WHERE v.Empresa = @Empresa
          AND v.fecha < @FechaAlbarán + 1
          AND v.PeriodoFacturación = @PeriodoFacturación
          AND ISNULL(c.SuPedido, '') <> ''
        GROUP BY v.[Nº Cliente], v.Contacto
        HAVING COUNT(DISTINCT c.SuPedido) > 1

        RAISERROR ('No se pueden agrupar albaranes del cliente %s (%s) porque tienen diferentes números de pedido del cliente (P.O.)', 11, 1, @clienteConflicto, @contactoConflicto)
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
          AND ISNULL(c.SuPedido, '') <> ''
        GROUP BY v.[Nº Cliente], v.Contacto
        HAVING COUNT(DISTINCT c.SuPedido) > 1
    )
    BEGIN
        RAISERROR ('No se pueden agrupar albaranes del cliente %s (%s) porque tienen diferentes números de pedido del cliente (P.O.)', 11, 1, @NumCliente, @Contacto)
        RETURN -1
    END
END

-- Declaración de variables
declare @Exito as bit
declare @NumPedido as int

if @NumCliente = ''
    declare crsCabVta cursor local fast_forward for select [nº cliente],contacto,min(NºPedido) as NºPedido from vstAgruparAlbaranesVta where Empresa = @Empresa and fecha < @FechaAlbarán+1 and PeriodoFacturación = @PeriodoFacturación group by [nº cliente],contacto order by [Nº Cliente]
else
    declare crsCabVta cursor local fast_forward for select [nº cliente],contacto,min(NºPedido) as NºPedido from vstAgruparAlbaranesVta where Empresa = @Empresa and fecha < @FechaAlbarán+1 and PeriodoFacturación = @PeriodoFacturación and [Nº cliente] = @NumCliente and Contacto = @Contacto group by [nº cliente],contacto

-- Inicialización de variables
set @Exito = 1

-- COMIENZO TRANSACCIÓN
begin transaction
    open crsCabVta
    fetch next from crsCabVta into @NumCliente,@Contacto,@NumPedido
    while (@@fetch_status = 0) and (@Exito = 1) begin
        update pedidosespeciales set nºpedidovta=null where empresa=@empresa and nºpedidovta in (select NºPedido from VSTAGRUPARALBARANESVTA where Empresa = @Empresa and fecha <= @FechaAlbarán +1 and PeriodoFacturación = @PeriodoFacturación and [Nº Cliente] = @NumCliente and Contacto = @Contacto)
        if @@error != 0 begin
            raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
            set @Exito = 0
        end

        update CabPedidoVta set Agrupada = 1,fecha=@FechaAlbarán where Empresa = @Empresa and Número = @NumPedido
        if @@error != 0 begin
            raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
            set @Exito = 0
        end
        update vstAgruparAlbaranesVta set NºPedido = @NumPedido where Empresa = @Empresa and fecha <= @FechaAlbarán +1 and PeriodoFacturación = @PeriodoFacturación and [Nº Cliente] = @NumCliente and Contacto = @Contacto
        if @@error != 0 begin
            raiserror ('No se pudieron actualizar las líneas del cliente %s (%s)',11,1,@NumCliente,@Contacto)
            set @Exito = 0
        end
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
        fetch next from crsCabVta into @NumCliente,@Contacto,@NumPedido
    end

if @Exito=1
    commit transaction
else
    rollback transaction
close crsCabVta
deallocate crsCabVta
GO
