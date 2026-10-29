-- 29/10/26 (NestoAPI#733): el pedido 928217 (cliente 1056) se facturó el 15/10 en la empresa espejo (GB2601623) y el traspaso
-- de la API se llevó también las dos líneas que quedaban pendientes. En la 3 no hay stock ni fechas de extracto: la segunda
-- entrega no se puede albaranear. Se devuelven a la empresa 1, como se ha hecho siempre: cabecera copiada en la 1 (IVA en
-- blanco, como venía el pedido, para que al facturar vuelva a ir a la espejo) y las dos líneas en curso (45958 con su
-- picking 99962 y la de portes). Su reserva de hueco ya está en la 1. Después: Facturar rutas, o FacturarPedido(1, 928217).
-- Lanzar como sa en NV, DESPUÉS de publicar la API con NestoAPI#733 (si no, Facturar rutas se las volvería a llevar todas).
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM CabPedidoVta WHERE Empresa = '1' AND Número = 928217)
INSERT INTO CabPedidoVta ([Empresa],[Número],[Nº Cliente],[Contacto],[Fecha],[Forma Pago],[PlazosPago],[Primer Vencimiento],[IVA],
    [Vendedor],[Comentarios],[ComentarioPicking],[Periodo Facturacion],[Ruta],[Serie],[CCC],[Origen],[Agrupada],[MotivoDevolución],
    [ContactoCobro],[NoComisiona],[NotaEntrega],[vtoBuenoPlazosPago],[Operador],[FijarPrimerVto],[MantenerJunto],[ServirJunto],
    [Usuario],[Fecha Modificación],[TipoRectificativa],[SuPedido],[NoCobrarComisionReembolso],[AvisarConImporteAlCogerPicking],
    [ModoServicio],[ModoFacturacion],[PedidoOrigen],[AlbaranOrigen],[FechaEntregaAgenciaPrometida],[ServirPendienteAlHaberStock],
    [AvisoPendienteCompleto])
SELECT '1',[Número],[Nº Cliente],[Contacto],[Fecha],[Forma Pago],[PlazosPago],[Primer Vencimiento],NULL,
    [Vendedor],[Comentarios],[ComentarioPicking],[Periodo Facturacion],[Ruta],'NV',[CCC],[Origen],0,[MotivoDevolución],
    [ContactoCobro],[NoComisiona],[NotaEntrega],[vtoBuenoPlazosPago],[Operador],[FijarPrimerVto],[MantenerJunto],[ServirJunto],
    SUSER_SNAME(),GETDATE(),[TipoRectificativa],[SuPedido],[NoCobrarComisionReembolso],[AvisarConImporteAlCogerPicking],
    [ModoServicio],[ModoFacturacion],[PedidoOrigen],[AlbaranOrigen],[FechaEntregaAgenciaPrometida],[ServirPendienteAlHaberStock],
    [AvisoPendienteCompleto]
FROM CabPedidoVta WHERE Empresa = '3' AND Número = 928217;

UPDATE LinPedidoVta SET Empresa = '1'
WHERE Empresa = '3' AND Número = 928217 AND Estado < 2 AND [Nº Orden] IN (328905500, 329495300);
IF @@ROWCOUNT <> 2 BEGIN RAISERROR('Se esperaban 2 líneas: no se cambia nada.', 16, 1); ROLLBACK; RETURN; END

SELECT Empresa, [Nº Orden], Producto, Estado, Picking FROM LinPedidoVta WHERE Número = 928217 ORDER BY Empresa, [Nº Orden];
COMMIT;
