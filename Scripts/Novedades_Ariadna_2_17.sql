/*
    Novedades de Ariadna 2.17 (23/10/26): Ariadna#51 corte 2 (lo leído al recibir se guarda en el servidor y lo ven
    todos los equipos y Nesto), #53 (error de red al terminar: aviso claro y la pantalla como estaba), #54 (segunda
    confirmación al recibir entera con menos de la mitad leído) y #55 (la cámara lee la etiqueta siguiente aunque la
    anterior siga en el encuadre; sugerencia 758).
    Lo de #51 necesita la API publicada con 04a21f22 y el script Ariadna51_RecepcionesLecturasEnCurso (lanzado 23/10).
    Sin la API nueva, Ariadna funciona como en la 2.16.
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.17 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título).
    Al publicar: comentario con @Andre @Pedro @Santiago @Alfredo @Paloma (recibir compartido, error al terminar, segunda
    confirmación), en la 758 (cámara) y marcar la 758 Implementada 2.17.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Recibir: todos ven lo mismo, también Nesto',
 N'Lo que lee al recibir se guarda en el servidor al momento. Si la misma recepción está abierta en otro equipo o en Nesto, cada uno ve al instante lo que leen los demás, y en la tarjeta sale «También está recibiendo: …». Se puede empezar en un equipo y terminar en otro con todo lo leído. Si se corta la wifi, lo leído se guarda en el equipo y se envía solo al volver la conexión.',
 N'Almacén,Tiendas'),
(2, N'Mejorado', N'Terminar con un error de red: la pantalla queda como estaba',
 N'Si al terminar falla la red o el servidor, ya no se pone todo en naranja: sale «NO SE HA PODIDO TERMINAR» con «Volver a intentarlo», y lo leído sigue ahí.',
 N'Almacén,Tiendas'),
(3, N'Mejorado', N'Recibir: aviso si se ha leído menos de la mitad',
 N'Al recibir entera una reposición con menos de la mitad de los productos leídos, Ariadna pregunta otra vez: «Seguir leyendo» (Intro o 1) o terminar igualmente (2). Así no se termina por error con casi nada leído.',
 N'Almacén,Tiendas'),
(4, N'Corregido', N'Cámara: lee la etiqueta siguiente sin salir de la pantalla',
 N'Con varias etiquetas en el encuadre, la cámara lee la que todavía no se ha contado, la más cercana al centro. Ya no hace falta salir al menú para que lea la siguiente.',
 NULL);
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.17', '2026-10-23', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
