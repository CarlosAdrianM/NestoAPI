-- 30/10/26 (sugerencia 899 de MariaJose, NestoAPI#753): el curso del pedido 929655 (cliente 35100) se vendió desde la
-- plantilla por TRANSFERENCIA, pero la API generó el enlace de tarjeta (PagoTPV 976) y dejó la inscripción 14 con FormaPago
-- «Tarjeta». Se deja como habría quedado con el arreglo: FormaPago 'Transferencia', sin enlace, y el enlace anulado
-- (Estado 'Cancelado', como hace la API al anular enlaces de un pedido, NestoAPI#734: la página /pago/{token} ya no deja pagarlo).
-- Lanzar como sa en NV. Con @Aplicar = 0 solo mira (no cambia nada); con 1, aplica dentro de una transacción.

DECLARE @Aplicar bit = 0;

DECLARE @Inscripcion int = 14;
DECLARE @PagoTPV int = 976;
DECLARE @Pedido int = 929655;
DECLARE @Cliente nvarchar(10) = '35100';

PRINT '--- Antes ---';
SELECT Id, EventoId, Cliente, Contacto, Estado, FormaPago, PagoTPVId, FechaLimitePago, Modalidad, FechaModificacion
FROM dbo.EventosInscripciones WHERE Id = @Inscripcion;
-- Debe salir: Cliente 35100, Estado Reservada, FormaPago Tarjeta, PagoTPVId 976.

SELECT Id, Empresa, Cliente, Tipo, Estado, Importe, Descripcion, Documento, FechaCreacion, FechaActualizacion
FROM dbo.PagosTPV WHERE Id = @PagoTPV OR PagoOriginalId = @PagoTPV;
-- Debe salir el 976 Pendiente (Descripcion «Pago pedido 929655 cursos», Documento INS14). Si sale Autorizado (ya ha pagado con
-- tarjeta), NO aplicar: la inscripción ya estará Pagada y el script no tiene sentido.

SELECT InscripcionId, EmpresaPedido, Pedido, LineaPedido, Estado
FROM dbo.EventosInscripcionesCurso WHERE InscripcionId = @Inscripcion;
-- Debe salir el pedido 929655 (EnCurso). No se toca.

SELECT Id FROM dbo.EventosInscripciones WHERE PagoTPVId = @PagoTPV;
-- Solo la 14 (si salen más, son del mismo pedido de cursos: revisar antes de aplicar).

IF @Aplicar = 1
BEGIN
    BEGIN TRANSACTION;

    UPDATE dbo.EventosInscripciones
    SET PagoTPVId = NULL, FormaPago = 'Transferencia', FechaModificacion = GETDATE()
    WHERE Id = @Inscripcion AND PagoTPVId = @PagoTPV AND Estado = 'Reservada' AND Cliente = @Cliente;
    -- Debe decir: 1 fila afectada.

    UPDATE dbo.PagosTPV
    SET Estado = 'Cancelado', FechaActualizacion = GETDATE()
    WHERE Id = @PagoTPV AND Estado = 'Pendiente';
    -- Debe decir: 1 fila afectada (0 si ya no estaba Pendiente: entonces no se toca).

    IF (SELECT COUNT(*) FROM dbo.EventosInscripciones WHERE Id = @Inscripcion AND PagoTPVId IS NULL AND FormaPago = 'Transferencia') = 1
        COMMIT TRANSACTION;
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'No se ha aplicado: la inscripción 14 no estaba como se esperaba (Reservada, cliente 35100, enlace 976).';
    END

    PRINT '--- Después ---';
    SELECT Id, Estado, FormaPago, PagoTPVId, FechaModificacion FROM dbo.EventosInscripciones WHERE Id = @Inscripcion;
    SELECT Id, Estado, FechaActualizacion FROM dbo.PagosTPV WHERE Id = @PagoTPV;
    -- Debe salir: inscripción 14 FormaPago Transferencia, PagoTPVId NULL; enlace 976 Cancelado.
END

-- Después: la clienta paga por transferencia con el concepto «Cliente 35100 - Pedido 929655»; la señal (#591/#615) o «Marcar
-- pagada» la pasa a Pagada (Transferencia) y FacturacionCursoEvento factura la línea del pedido 929655. Si MariaJose quiere
-- mandarle los datos de la transferencia, en Nesto el pedido 929655 tiene «Copiar datos de transferencia» (sugerencia 864).
