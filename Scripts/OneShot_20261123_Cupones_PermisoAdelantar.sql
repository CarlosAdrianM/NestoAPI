/*
    23/11/26 (NestoAPI#885). Permiso para adelantar cupones de evento, decidido por Carlos:
      Manuel → «1» (cualquier cliente) · Sancho (Alberto) → «Equipo» (solo clientes de los vendedores de su equipo).
    Dirección no necesita fila. Lanzar como sa. Idempotente: si la fila existe, deja el valor de aquí.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;

DECLARE @filas TABLE (Usuario varchar(50), Valor varchar(50));
INSERT INTO @filas VALUES ('Manuel', '1'), ('Sancho', 'Equipo');

BEGIN TRANSACTION;
UPDATE p SET Valor = f.Valor, Usuario2 = 'sa', [Fecha Modificación] = GETDATE()
FROM ParametrosUsuario p JOIN @filas f ON f.Usuario = RTRIM(p.Usuario)
WHERE p.Empresa = '1' AND p.Clave = 'Cupones.PuedeAdelantar' AND RTRIM(ISNULL(p.Valor, '')) <> f.Valor;

INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
SELECT '1', 'Cupones.PuedeAdelantar', f.Usuario, f.Valor, 'sa', GETDATE()
FROM @filas f
WHERE NOT EXISTS (SELECT 1 FROM ParametrosUsuario p WHERE p.Empresa = '1' AND p.Clave = 'Cupones.PuedeAdelantar' AND RTRIM(p.Usuario) = f.Usuario);
COMMIT TRANSACTION;

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, [Fecha Modificación]
FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Cupones.PuedeAdelantar' ORDER BY 1;
