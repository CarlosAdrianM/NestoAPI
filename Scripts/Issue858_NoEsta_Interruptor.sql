/*
    NestoAPI#858 (Carlos 12/11/26): «No está» = el hueco a cero en el inventario, en el picking (PICK) y en la reposición (REPO),
    ENCENDIDO. El código va encendido aunque esta fila NO exista: este script solo la deja a la vista (valor '1') para que el apagado
    de emergencia sea cambiar un valor, sin publicar.
    Interruptor: ParametrosUsuario empresa '1', usuario '(defecto)', clave 'Salidas.NoEstaRegulariza'.
      '1' (o vacío, o sin fila) = encendido.
      '0' (o false/no/off/apagado) = apagado: POST api/Almacen/Recogidas/{tipo}/{n}/NoEsta contesta Regularizado = false y la PDA
      da la falta como siempre. Vale al momento (se lee en cada «No está», sin caché).
    Apagado de emergencia:
      UPDATE ParametrosUsuario SET Valor = '0', Usuario2 = 'NoEsta858', [Fecha Modificación] = GETDATE()
      WHERE Empresa = '1' AND Clave = 'Salidas.NoEstaRegulariza' AND Usuario = '(defecto)';
    Idempotente: si la fila ya existe no la toca (no vuelve a encender lo que alguien haya apagado). Se puede lanzar antes o después
    de publicar. Sin GRANT nuevo. El appSettings «Salidas.NoEstaRegulariza» del Web.config ya no se lee.
*/

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Salidas.NoEstaRegulariza' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Salidas.NoEstaRegulariza', '(defecto)', '1', 'NoEsta858', GETDATE());
GO

SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'Salidas.NoEstaRegulariza';
GO
