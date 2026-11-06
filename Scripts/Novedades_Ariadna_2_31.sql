/*
    Novedades de Ariadna 2.31 (06/11/26): Ariadna#98 (botones +N, +1 y −1 en el inventario; sugerencias 1104 de Andre, 1109 de Santiago y
    1111 de Alfredo), parte Ariadna de NestoAPI#839 (varias unidades sin etiqueta de una vez; sugerencia 1113 de Reina, ámbito Nesto) y de
    NestoAPI#831 (furgoneta por fecha de reparto). Necesita la API del 06/11 (master tras faa68ea6).
    Ejecutar en SSMS contra NV como sa cuando la 2.31 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
    NO duplicar: las sugerencias 1104, 1109 y 1111 (Ariadna#98) las marca Implementadas en 2.31 el OneShot (la sugerencia ES la novedad);
    aquí no llevan fila.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Gracias, Andre, Santiago, Alfredo y Reina',
 N'Los botones para sumar unidades en el inventario sin saberse el atajo, y apuntar varias unidades sin etiqueta de una vez al recibir, salen de lo que nos habéis contado hoy por «Avisar». Seguid contándonos.',
 NULL),
(2, N'Nuevo', N'Recibir: varias unidades sin etiqueta de una vez',
 N'Al apuntar algo que llega sin etiqueta, después del motivo se pregunta «¿Cuántas?» si falta más de una: teclee la cantidad e Intro (Intro solo = 1), o use − / + / «Todas» en la pantalla. Se apuntan todas en una sola operación y Algete y Compras reciben un solo aviso con la cantidad. No deja apuntar más de las que faltan.',
 N'Almacén,Tiendas'),
(3, N'Nuevo', N'Reparto: los bultos se cargan por su fecha de reparto',
 N'La etiqueta de reparto propio ya se puede sacar al cerrar el bulto aunque el reparto sea otro día. En la lista de reparto, las paradas que no son del día llevan «Reparto del dd/MM». Al cargar la furgoneta, un bulto de otro día se rechaza: «Este bulto es del reparto del dd/MM: no se carga hoy.»',
 N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.31', '2026-11-06', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
