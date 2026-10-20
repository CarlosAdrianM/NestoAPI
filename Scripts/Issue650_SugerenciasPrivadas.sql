/*
    NestoAPI#650 (Carlos, 15/10/26): sugerencias «privadas». Solo las ven quien la crea y Dirección / Informática.

    Ejecutar en SSMS contra NV (NestoConnection), como sa. Idempotente: se puede volver a lanzar.

    ORDEN: este script → API → clientes (Nesto, Ariadna; NestoApp más adelante).
    Si la API se publica antes que el script, nada falla: la API lee y escribe la columna con SQL dinámico
    (COL_LENGTH), así que mientras no exista no hay ninguna privada y todo sale como siempre. Lo único que no
    se puede es crear una privada (ni pasar una a privada): el POST/PUT contesta 400 con «Todavía no se pueden
    enviar sugerencias privadas. Envíela como pública o inténtelo de nuevo más tarde.» y no se crea nada.

    QUÉ HACE
      Columna Novedades.Privada bit NOT NULL DEFAULT 0. Todas las de antes quedan públicas.

    REGLAS (en la API: PrivacidadNovedades y NovedadesController)
      - Una privada no la devuelve ningún GET a quien no sea su autor o de Dirección / Informática: ni
        api/Novedades, ni Sugerencias, ni Buscar, ni sus comentarios, imagen, adjuntos o imágenes de comentarios
        (404, como si no existiera). Tampoco se puede comentar (404).
      - No admite «Me gusta» (PUT .../Voto con 1 o -1 → 400; quitar un voto antiguo con 0, sí).
      - En la lista de sugerencias va detrás de las públicas.
      - Avisos (@menciones, actividad al supervisor, respuestas del asistente): solo a su autor y a los
        miembros de los grupos del dominio Dirección e Informatica.
      - POST api/Novedades/Sugerencias admite "Privada": true (opcional; sin él, pública como siempre).
      - PUT api/Novedades/Sugerencias/{id} admite "Privada": true | false (Dirección / Informática).
*/

USE NV;
GO

IF COL_LENGTH('dbo.Novedades', 'Privada') IS NULL
    ALTER TABLE dbo.Novedades ADD Privada bit NOT NULL
        CONSTRAINT DF_Novedades_Privada DEFAULT (0);
GO

-- Sin índice a propósito: la tabla tiene unos cientos de filas y la API lee las privadas con un SELECT por
-- petición. (Un índice filtrado obligaría a que todo lo que escriba en Novedades lleve las opciones SET de rigor.)

-- La API ya tiene SELECT, INSERT y UPDATE sobre la tabla Novedades (la columna nueva los hereda): no hace
-- falta ningún GRANT.

-- Comprobación
SELECT COL_LENGTH('dbo.Novedades', 'Privada') AS LongitudColumnaPrivada,
       (SELECT COUNT(*) FROM dbo.Novedades WHERE Privada = 1) AS Privadas;
