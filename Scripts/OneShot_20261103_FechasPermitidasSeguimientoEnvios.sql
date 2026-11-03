/*
    03/11/26: el job de seguimiento de envíos contabiliza (prdContabilizar @Usuario = 'SeguimientoEnvios') y FechasPermitidas
    no tiene fila para ese usuario → «Fecha 03/11/2026 no permitida (anterior a la permitida) para SeguimientoEnvios»
    (también el 19/10). Se le da la misma ventana de contabilidad que al usuario NestoAPI; la tarea mensual la ampliará
    con las demás. Lanzar como sa en NV.
*/
USE NV;
IF NOT EXISTS (SELECT 1 FROM dbo.FechasPermitidas WHERE Empresa = '1' AND RTRIM(Usuario) = 'SeguimientoEnvios')
    INSERT INTO dbo.FechasPermitidas (Empresa, Usuario, FechaInicialCnt, FechaFinalCnt, Usuario2)
    SELECT '1', 'SeguimientoEnvios', FechaInicialCnt, FechaFinalCnt, 'Carlos' FROM dbo.FechasPermitidas WHERE Empresa = '1' AND RTRIM(Usuario) = 'NestoAPI';
SELECT RTRIM(Usuario) Usuario, FechaInicialCnt, FechaFinalCnt FROM dbo.FechasPermitidas WHERE RTRIM(Usuario) IN ('NestoAPI', 'SeguimientoEnvios');
