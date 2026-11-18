/*
    Nesto (Carlos 18/11/26, continuación de la sugerencia 1331): tamaño de letra del nombre y la dirección del cliente que
    pinta el selector de clientes de Nesto debajo de la caja de búsqueda, en todas las pantallas que lo usan.
    Parámetro de usuario: ParametrosUsuario empresa '1', clave 'TamanoLetraSelectorCliente'.
      'Normal'    = 10 puntos, el de siempre (y el más pequeño).
      'Grande'    = 13 puntos.
      'MuyGrande' = 16 puntos.
      Sin fila, sin red o con cualquier otro valor = Normal.
    Nesto lo lee una vez al arrancar y cada usuario lo cambia en la ruedita del propio selector («Tamaño del nombre del
    cliente»), que guarda SU fila por el PUT de api/ParametrosUsuario. Ese PUT solo actualiza (sin la fila del usuario da
    NotFound) y el GET crea la fila del usuario copiando la «(defecto)»: por eso hace falta esta fila.
    BD: NV. Sin GRANTs (INSERT de datos). Idempotente: si la fila ya existe no la toca. Se puede lanzar antes o después de
    publicar Nesto (sin la fila, todos ven Normal y la ruedita no puede guardar).
*/

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'TamanoLetraSelectorCliente' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'TamanoLetraSelectorCliente', '(defecto)', 'Normal', 'Nesto', GETDATE());
GO

-- VERIFICACIÓN:
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'TamanoLetraSelectorCliente';
GO
