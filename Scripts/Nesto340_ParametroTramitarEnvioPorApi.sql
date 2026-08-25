-- Nesto#340 (Agencias, slice A4.1 — 25/08/26): cerrar el envio y contabilizar su reembolso
-- contra NestoAPI (POST api/EnviosAgencias/{id}/ConfirmarTramitacion) en vez de por Entity
-- Framework desde el cliente.
--
-- Se activa SOLO para el usuario piloto. Sin fila (o con otro valor) Nesto sigue usando el
-- camino de siempre, que se queda intacto debajo: vuelta atras = DELETE de esta fila, sin
-- publicar nada. Protocolo de pies de plomo acordado el 20/08/26 tras los 3 sustos de A2.
--
-- ATENCION: este flujo CONTABILIZA (prdContabilizar). Antes de extenderlo a mas usuarios,
-- tramitar un envio con reembolso y comprobar el asiento en el diario _Reembolso: mismo
-- importe, misma contrapartida y, si liquidaba un movimiento del extracto, mismo Liquidado.

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'TramitarEnvioPorApi' AND Usuario = 'NUEVAVISION\Carlos')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'TramitarEnvioPorApi', 'NUEVAVISION\Carlos', 'API', 'NUEVAVISION\Carlos', GETDATE());
    PRINT 'Parametro TramitarEnvioPorApi activado para el piloto.';
END
ELSE
BEGIN
    PRINT 'El parametro ya existia: nada que hacer.';
END
