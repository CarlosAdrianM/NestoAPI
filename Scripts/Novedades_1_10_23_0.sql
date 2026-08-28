/*
    Novedades de la versión 1.10.23.0 (28/08/2026).

    Sube la 3ª cifra porque la ficha de producto estrena una pestaña entera con dos
    mantenimientos que antes no se podían hacer sin entrar a la base de datos.

    SUSTITUYE a Scripts/Novedades_1_10_22_6.sql, que se preparó por la mañana para una versión
    que ya no se va a publicar y que NO llegó a ejecutarse. Su entrada (la hoja del almacén) se
    ha traído aquí.

    SE OMITEN A PROPÓSITO (no son perceptibles para el usuario):
      - #421: el campo ExclusivoProfesional viajando por el bus y el endpoint de mantenimiento.
        Lo que sí se cuenta es la casilla, que es lo que el usuario ve.
      - Nesto#456: el código del subgrupo en el DTO de la ficha (SubgrupoCodigo), interno.
      - #422: issue de deuda técnica abierta hoy, sin código.
      - La salida de los productos de las categorías EP está PARADA esperando al módulo de
        PrestaShop (prestashop-nestosync#22), así que todavía no hay nada que contar de eso.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.23.0', '2026-08-28', 'Nuevo', 'Pestana Web en la ficha del producto',
 'La ficha de producto tiene una pestana nueva, "Web", con las dos cosas que definen al producto en la tienda online y que hasta ahora solo se podian cambiar por dentro. La primera es marcar un producto como exclusivo profesional: en la tienda se sigue viendo, pero quien no sea profesional no ve el precio ni puede comprarlo, le sale un aviso para crear cuenta. La segunda son las categorias web adicionales en las que aparece el producto, que se pueden anadir, quitar y reordenar (el orden es el que se ve en la tienda). La categoria de siempre del producto se muestra arriba y no se toca. La ven Compras y Tienda Online.', 'Nesto', 1, 'sa'),

('1.10.23.0', '2026-08-28', 'Corregido', 'Ya se pueden imprimir otra vez las etiquetas de agencia',
 'Desde el cambio interno de ayer, al imprimir la etiqueta de cualquier pedido saltaba un error tecnico ("Sequence contains no matching element") y no se podia sacar. El motivo era un detalle de como viajaba el codigo de empresa despues de mover las agencias al servidor. Ya funciona con normalidad. De paso, este tipo de fallos de la ventana de Agencias queda ahora registrado en el sistema de errores: antes se quedaban en el aviso de pantalla y no llegaba constancia a informatica.', 'Nesto', 1, 'sa'),

('1.10.23.0', '2026-08-28', 'Corregido', 'La hoja del almacen ya no se bloquea en los pedidos con cupon de descuento',
 'Desde el dia 25 el sistema revisa, antes de imprimir la hoja del almacen, que ninguna linea tenga reservadas mas unidades de las pedidas, para que no se sirva de mas sin que nadie lo note. Esa revision se equivocaba con las lineas de cantidad negativa (el cupon de descuento y las devoluciones): las tomaba por descuadradas y no dejaba sacar la hoja, aunque el pedido estuviera perfecto. Ya no las tiene en cuenta, y el aviso sigue saltando cuando el descuadre es de verdad.', 'NestoAPI', 1, 'sa');

SELECT @@ROWCOUNT AS Insertadas;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.23.0' ORDER BY Id;
