/*
    Novedades de la versión 1.10.29.1 (23/09/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.29.1). NO se sube la tercera cifra: son arreglos (el
    incidente de CPU de esta mañana, #517) y mejoras de algo que ya existía. La retirada de Entity
    Framework de Nesto (Nesto#340) no la ve el usuario.

    ORDEN:
      1) Publicar NestoAPI (lleva el arreglo de fondo de las sugerencias, CTT por lotes, Bancos rápido).
      2) Publicar la ClickOnce 1.10.29.1 desde Clean + Rebuild (en bin quedan EntityFramework.dll viejos).
      3) ESTE script. Idempotente: compara por Versión + Título.
      4) Opcional: Scripts/Issue517_InterruptorOfertasSugeridas.sql (crea el interruptor a '1'; sin la
         fila también está encendido. Sirve para tener a mano el UPDATE de apagado).

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - Nesto sin Entity Framework ni acceso directo a la base de datos (Nesto#340, partes 1-3).
      - Que la ventana «Premio Deuda» desaparece: no la usaba nadie (el premio ya no existe).
      - Las defensas de #517 (caché, interruptor, huella del pedido): se cuentan por su efecto.
      - CTT por lotes y el 429 de cupo; los jobs de Nesto_sync sin solaparse; el código 0500 de CTT.
      - Tests, issues.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.29.1'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-23';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Corregido', N'Vuelve el aviso de ofertas de la plantilla, y ya no ralentiza el servidor',
     N'Esta mañana hubo que apagar el aviso de ofertas no aplicadas porque cada consulta era tan pesada que, con muchas plantillas abiertas a la vez, el servidor se quedaba sin CPU y todo iba lento. Se ha rehecho para que cada consulta cueste muy poco, la plantilla ya no pregunta dos veces por el mismo pedido y, si hiciera falta, se puede apagar al momento sin sacar versión. El aviso vuelve a salir igual que ayer.'),
    ('Corregido', N'El aviso de ofertas ya no propone el N+M de la familia en productos sin descuento',
     N'En productos que no admiten descuento (por ejemplo el 45917 de Anubis) el aviso proponía el 6+1 de su familia, que no se les puede aplicar. Ahora solo se les propone una oferta si está dada de alta expresamente para ese producto.'),
    ('Mejorado', N'La ventana de Bancos abre mucho más rápido',
     N'Al abrir una cuenta con muchos movimientos (La Caixa, por ejemplo) había que esperar más de diez segundos: el estado de punteo se preguntaba movimiento a movimiento. Ahora se calcula de una vez y la ventana abre en uno o dos segundos.'),
    ('Mejorado', N'Al cambiar el reembolso de un envío ya tramitado, Nesto avisa de que la agencia no se entera',
     N'En la pestaña Tramitados de Agencias, cambiar el reembolso solo lo cambia en Nesto: el paquete ya lo tiene la agencia y el cambio lo tienen que hacer ellos cuando se lo pedimos. Ahora el mensaje de confirmación lo dice claramente, para hacerlo solo cuando la agencia ya lo ha confirmado.'),
    ('Corregido', N'Los envíos de CTT ya no vuelven solos a «En curso»',
     N'Cuando CTT recogía los paquetes, el seguimiento automático los devolvía de Tramitados a En curso, como si no se hubieran tramitado. Ya no pasa: en tránsito o en reparto siguen en Tramitados hasta que se entregan.'),
    ('Corregido', N'Las líneas añadidas al ampliar un pedido ya se pueden facturar',
     N'Al ampliar un pedido, las líneas nuevas se quedaban sin visto bueno y el pedido no se podía facturar hasta arreglarlo a mano (pasaba sobre todo en tiendas). Ahora todas las líneas nacen con visto bueno.'),
    ('Corregido', N'El informe de saldo de la cuenta 555 ya se puede abrir',
     N'La pantalla del saldo de la 555 daba error siempre al abrirla. Ya funciona.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 7 filas, una por novedad, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
