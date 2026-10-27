-- Nesto#490 (panel lateral, tramo 1, 27/10/26): el menú lateral de Nesto (NavigationView) en lugar de la cinta.
-- Nesto lee NavegacionLateral del usuario al crear la ventana principal: con '1', el menú lateral; con cualquier otro
-- valor, sin fila o si no se puede leer, la cinta de siempre. Se lee una vez por arranque: para cambiar de uno a otro
-- hay que cerrar y volver a abrir Nesto.
-- Cada usuario lo puede cambiar él mismo desde Nesto → Parámetros (catálogo de ServicioParametrosEditables, «Diseño
-- nuevo: menú lateral en lugar de la cinta»): la fila (defecto) es lo que ven los que no lo han tocado.
-- Plan: (defecto) = '0' ahora; en unas dos semanas, (defecto) = '1' (dejando '0' como vuelta atrás); después se quita la cinta.
-- NavegacionLateralCompacta (si el panel sale abierto o solo con iconos): la guarda el propio Nesto cuando el usuario
-- abre o cierra el panel con su botón. Tramo 2 (27/10/26): SÍ necesita la fila (defecto) = '1' (compacto, decisión 2).
-- Sin ella no se recordaba nada: el PUT de api/ParametrosUsuario solo actualiza (sin la fila del usuario devuelve
-- NotFound) y el GET solo crea la fila del usuario copiando la de (defecto). Con la fila (defecto), el GET del
-- arranque crea la del usuario y el PUT ya tiene qué actualizar.
--
-- BD: NV. Sin GRANTs (INSERT de datos). Idempotente. Ejecutar antes o después del deploy (sin fila → la cinta).

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NavegacionLateral' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NavegacionLateral', '(defecto)', '0', 'NestoAPI', GETDATE());
END
GO

-- Piloto (Carlos, 27/10/26): usuario como lo guardan los demás parámetros («Carlos», sin dominio).
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NavegacionLateral' AND Usuario = 'Carlos')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NavegacionLateral', 'Carlos', '1', 'NestoAPI', GETDATE());
GO

-- Tramo 2: el panel compacto por defecto, y que se pueda recordar abierto o compacto por usuario
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NavegacionLateralCompacta' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NavegacionLateralCompacta', '(defecto)', '1', 'NestoAPI', GETDATE());
GO

-- VERIFICACIÓN:
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Clave IN ('NavegacionLateral', 'NavegacionLateralCompacta');
GO
