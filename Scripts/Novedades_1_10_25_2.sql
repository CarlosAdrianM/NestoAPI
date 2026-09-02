/*
    Novedades de la versión 1.10.25.2 (02/09/2026, tarde).

    Solo sube la última cifra: una corrección en Nesto y trabajo de servidor para la app y la tienda.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario de Nesto):
      - Cobro directo con la tarjeta guardada en la app de clientes (NestoAPI#178): Comercia ha
        activado la operativa MIT y el servidor cobra con el token; si el terminal todavía
        contesta SIS0883 el cliente confirma en la pasarela con su tarjeta cargada. Es de la app.
      - Niveles "pedidos sin ver precios" (cargo 30) y "sin ver descuentos" (cargo 31) de las
        personas de contacto (NestoAPI#446) y su gestión por el titular desde la app (#447):
        de momento solo desde la app; en la ficha de Nesto los cargos ya existían.
      - El concepto del prepago de los pedidos de la app (#436): solo se ve en cartera.
      - Buscador paginado para la tienda PrestaShop (api/buscador/paginado) y los endpoints de
        búsqueda marcados como públicos: servidor a servidor.
      - Refactor de AgenciaService (Nesto#340 A3): sin cambio visible.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.25.2', '2026-09-02', 'Corregido', 'La ficha de cliente ya no da error al cargar las deudas si el teléfono está vacío',
 'Al abrir un cliente con el teléfono en blanco, la pestaña de deudas fallaba con un error interno (referencia nula) y no llegaba a mostrar nada. Ahora un teléfono vacío se trata como "sin teléfono" y las deudas se cargan con normalidad.', 'Nesto', 1, 'sa');

SELECT Version, Titulo FROM Novedades WHERE Version = '1.10.25.2';
