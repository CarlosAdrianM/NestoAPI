/*
    NestoAPI#577 (corte 3b): quién puede rellenar (crear) reposiciones a mano.

    Decisión de Carlos (08/10/26): «Solo el proceso automático rellena reposiciones. A mano, solo quien tenga el permiso
    especial: Manuel, Alfredo y Carlos. El resto trabaja sobre las que ya están rellenas.»

    POST api/Reposiciones (crear) devuelve 403 («Solo el proceso automático y las personas autorizadas pueden rellenar
    reposiciones a mano.») a quien no esté en la lista; GET api/Reposiciones/PuedeRellenarManual dice si el usuario actual
    está, para que Nesto y Ariadna escondan el botón. El job «reposiciones-automaticas» no pasa por aquí.

    La API lee SOLO la fila «(defecto)» de la empresa 1 (no hay filas por usuario: cada uno podría cambiarse la suya).
    Lista separada por comas (o punto y coma) con el nombre de usuario SIN dominio: vale igual para Nesto
    (NUEVAVISION\Alfredo) que para Ariadna (Alfredo), sin distinguir mayúsculas. Sin la fila la API usa esta misma lista;
    con la fila vacía no puede nadie.

    Para añadir a alguien: UPDATE ParametrosUsuario SET Valor = 'Manuel, Alfredo, Carlos, Fulano' WHERE Empresa = '1'
    AND Usuario = '(defecto)' AND Clave = 'UsuariosRellenarReposicionManual';

    Idempotente. Se puede lanzar antes o después del deploy (sin la fila la API usa la lista por defecto).
*/

SET NOCOUNT ON;
USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'UsuariosRellenarReposicionManual' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'UsuariosRellenarReposicionManual', '(defecto)', 'Manuel, Alfredo, Carlos', 'NestoAPI', GETDATE());
END
GO

-- VERIFICACIÓN: una fila con 'Manuel, Alfredo, Carlos'
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario
WHERE Clave = 'UsuariosRellenarReposicionManual';
GO
