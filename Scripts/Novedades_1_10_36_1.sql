/*
    Novedades de la versión 1.10.36.1 (02/10/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.36.1).

    ORDEN:
      1) ANTES de publicar la API: nada (no hay SQL nuevo).
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.36.1 (Clean + Rebuild).
      4) DESPUÉS: ESTE script (idempotente: compara por Versión + Título).
      5) Scripts/OneShot_20261002_CerrarSugerencias_1_10_36_1.ps1: pone la versión a la sugerencia 462 de Laura
         (los pedidos que solo llevan cursos pasan solos a la serie CV) y le contesta en su hilo. NO se repite aquí.

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota):
      - Nesto#490: 4C.3 (ViewModelLocator fuera de 38 vistas, ventanas y selectores) y 4C.4 pasos 1-3 parciales
        (navegación por IServicioNavegacion), borrado de DialogServiceEnHiloUi. Sin cambio de comportamiento.
      - El arreglo de los comentarios en ExtractoRuta se cuenta abajo solo por su efecto (facturar rutas).
      - Tests, issues, refactorizaciones.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.36.1'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-10-02';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Mejorado', N'Si el cliente cierra el día de la entrega, el picking lo dice',
     N'Cuando un pedido no entraba en el picking porque el cliente cierra el día en que se le entregaría (por ejemplo, un cliente que cierra los lunes), Nesto decía que no había stock suficiente aunque lo hubiera. Ahora avisa de que el cliente cierra ese día y, en el picking de un solo pedido, pregunta «¿Aún así quieres asignarle picking?» por si se quiere sacar igualmente.'),
    ('Corregido', N'Facturar rutas ya no falla con las notas de entrega de ruta propia',
     N'Al facturar las rutas, una nota de entrega de una ruta propia con un comentario largo (por ejemplo, las notas automáticas de «pendiente de entregar del pedido…») hacía fallar el proceso y la nota no salía. Ya se procesan con normalidad.'),
    ('Corregido', N'Las ofertas combinadas con importe mínimo ya no regalan de más',
     N'En algunas ofertas combinadas con importe mínimo, Nesto daba por buena una cantidad de regalo mayor que la que permite la oferta. Ahora se comprueba que cada producto lleve la cantidad que le corresponde.'),
    ('Corregido', N'No se sugiere un 6+1 sobre productos con precio rebajado o con descuento',
     N'Nesto podía sugerir una oferta de unidades de regalo en líneas que ya llevaban un precio especial o un descuento. Como lo que se cobra en esas ofertas tiene que ir a precio de tarifa y sin descuento, ya no se sugiere en esos casos.'),
    ('Corregido', N'Pedidos de compra: se aplica bien el precio por cantidad del proveedor',
     N'Al usar «Ampliar hasta el stock máximo» en un pedido de compra, si el proveedor tenía varios precios según la cantidad, la línea salía con el del primer tramo. Ahora sale con el que corresponde a la cantidad pedida.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 5 filas de este script (más la 462 de Laura cuando se lance el paso 5), sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
