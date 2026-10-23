/*
    Novedades de la versión 1.10.48.0 de Nesto (23/10/2026).
    Versión de la ClickOnce: 1.10.48.0 (pubxml 1.10.48.* en revisión 0).

    Tanda: NestoAPI (Ariadna#51 corte 2 lecturas en curso compartidas, Nesto#552 pendientes de entregar, 756 Ganavisiones
    al modificar con la plantilla, correo del picking automático con clientes que cierran) + Nesto (Recibir reposición
    compartido con Ariadna, pantalla «Pendientes de entregar», 761 comentarios del rapport, 756, botón del cheque regalo
    en el pedido de origen o facturado [928889]).

    ORDEN:
      1) Scripts sa ANTES de publicar la API (YA LANZADOS el 23/10): Ariadna51_RecepcionesLecturasEnCurso,
         Sugerencia761_ParametroRapport.
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.48.0.
      4) DESPUÉS: ESTE script.

    SE OMITE A PROPÓSITO:
      - Correo del picking automático cuando solo había clientes que cierran: interno (almacén lo verá en el correo).
      - Lo de Ariadna 2.17 (#53, #54, #55): va en su propio script de Novedades de Ariadna.
      - Aplázame en la app y sus cuotas (TNV#91): es para los clientes (TNV).

    La entrada de agradecimiento a los key users (Carlos, 23/10/26: «agradecerles el feedback públicamente») sale de
    Novedades/NovedadesComentarios desde el 24/09: 94 sugerencias de usuarios (60 ya hechas) y 47 comentarios.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.48.0';
DECLARE @fecha DATE = '2026-10-23';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias a quienes nos ayudan a mejorar Nesto',
     N'Desde finales de septiembre nos han llegado 94 sugerencias y casi 50 comentarios desde «Novedades», y 60 de esas sugerencias ya están hechas. Muchas de las mejoras de cada versión salen de ahí. Gracias de corazón a todos, y en especial a nuestros «key users» de este mes: Paloma, Alfredo, Laura, Manuel, Sancho, María José, Marta, Andre, Enrique, Aida y Reina. Si algo no funciona o se le ocurre cómo mejorarlo, cuéntenoslo: lo leemos todo.',
     NULL),
    ('Nuevo', N'Pendientes de entregar: todas las líneas pendientes de todos los almacenes',
     N'En Clientes > Ventas hay un botón nuevo, «Pendientes de entregar». Enseña todas las líneas de pedido que quedan por entregar en Algete, Reina y Alcobendas, con el pedido, el cliente, el vendedor, la fecha de entrega, el producto, la cantidad y el importe. Puede filtrar por almacén, producto o vendedor, buscar con el filtro de siempre, abrir el pedido con doble clic y exportarlo a Excel. Gracias, Manuel, por la idea.',
     NULL),
    ('Mejorado', N'Recibir reposición: lo leído se comparte con Ariadna y no se pierde',
     N'Lo que lee al recibir una reposición se guarda en el servidor al momento. Si la misma reposición está abierta en Ariadna (o en otro Nesto), cada uno ve al instante lo que lee el otro, y arriba aparece quién más está recibiendo. Se puede leer una parte en Nesto y terminarla desde el móvil, o al revés. Si se corta la red al terminar, lo leído sigue guardado: basta con volver a pulsar «Terminar».',
     N'Tiendas,Almacén'),
    ('Mejorado', N'Rapport: el cuadro de comentarios ya se ve bien',
     N'En la ficha del rapport, el cuadro de «Comentarios» tiene siempre sitio de sobra. «Puede ofrecer» ocupa menos (se desplaza si hay más productos) y se puede plegar con el triángulo o con Alt+O: Nesto recuerda si lo quiere plegado en el rapport, aparte de la plantilla. Gracias, Paloma.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'El correo de incidencia de la agencia lleva el enlace de seguimiento',
     N'Los correos «Incidencia de la agencia en el pedido» llevan ahora el enlace para seguir el envío en la web de la agencia.',
     N'Vendedores,Tiendas,Administración'),
    ('Corregido', N'Ganavisiones al ampliar un pedido desde la plantilla',
     N'Al modificar un pedido con la plantilla, los productos que ya tenía el pedido cuentan para los Ganavisiones, y ya no sale «ha superado los Ganavisiones» por error. Gracias, Sancho.',
     N'Vendedores,Tiendas'),
    ('Corregido', N'Cheque regalo: el botón ya no sale en el pedido que lo genera',
     N'El botón «Añadir cheque regalo» ya no aparece en el pedido con el que el cliente ha ganado el cheque, ni en un pedido ya facturado.',
     N'Vendedores,Tiendas,Administración');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
