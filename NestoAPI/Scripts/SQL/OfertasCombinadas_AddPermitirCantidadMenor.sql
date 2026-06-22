-- Ofertas combinadas: casilla "Permitir cantidad menor" por línea (NestoAPI#239)
-- Añade PermitirCantidadMenor a OfertasCombinadasDetalle:
--   0 (por defecto) -> cantidad exacta (comportamiento actual intacto)
--   1               -> la Cantidad pasa a ser un MÁXIMO: el pedido puede llevar de 0 a
--                      Cantidad de ese producto sin que la oferta deje de validar; llevar
--                      MÁS sigue sin permitirse.
-- Caso de uso: oferta "Level Lash Sérum 6+2" con 20 folletos + 1 expositor como extras;
-- el cliente puede no necesitar tantos folletos (10 en vez de 20) o ya tener el expositor (0).
--
-- NestoConnection (BD de negocio). NO necesita GRANT: es un ALTER sobre una tabla
-- existente y los permisos de la tabla ya cubren la nueva columna.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.OfertasCombinadasDetalle')
      AND name = 'PermitirCantidadMenor'
)
BEGIN
    ALTER TABLE dbo.OfertasCombinadasDetalle
        ADD PermitirCantidadMenor BIT NOT NULL CONSTRAINT DF_OfertasCombinadasDetalle_PermitirCantidadMenor DEFAULT 0;
END
GO
