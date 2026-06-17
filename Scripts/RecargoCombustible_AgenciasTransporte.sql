-- Recargo de combustible (fuel) por agencia, editable mensualmente desde la UI.
-- Se guarda como FRACCIÓN (0.1055 = 10,55 %). Se aplica en AgenciasViewModel.CalcularCostoEnvio
-- (porte * (1 + RecargoCombustible)) a TODAS las agencias, y se elimina el FACTOR_FUEL=1.025
-- hardcodeado de TarifaInnovatransBase, para que la comparativa no quede sesgada.
--
-- BD: NV (NestoConnection / negocio). Es un ALTER de tabla existente -> no requiere GRANT nuevo.
-- Tras ejecutarlo: REGENERAR EL EDMX (NestoAPI y Nesto) para que aparezca la propiedad.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('AgenciasTransporte') AND name = 'RecargoCombustible'
)
BEGIN
    ALTER TABLE AgenciasTransporte
        ADD RecargoCombustible decimal(6, 4) NOT NULL CONSTRAINT DF_AgenciasTransporte_RecargoCombustible DEFAULT 0;
END
GO

-- Valores iniciales conocidos (el usuario los irá ajustando mensualmente desde la UI):
--  - GLS/ASM (Numero = 1): carburante 9,05 % (mayo 2026) + climat protec 1,5 % = 10,55 %  -> 0.1055
--  - El resto se deja a 0 hasta confirmar su fuel (Sending/CEX/OnTime) e Innovatrans (2,5%).
UPDATE AgenciasTransporte SET RecargoCombustible = 0.1055 WHERE Numero = 1;
GO
