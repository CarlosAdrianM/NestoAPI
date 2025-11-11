# Script PowerShell para aplicar cambios en GestorFacturacionRutas.cs

$file = "C:/Users/Carlos/source/repos/NestoAPI/NestoAPI/Infraestructure/Facturas/GestorFacturacionRutas.cs"

# Leer el archivo
$content = Get-Content $file -Raw

# Cambio 1: Modificar firma de GenerarDatosImpresionAlbaran
$content = $content -replace `
    'private DocumentoParaImprimir GenerarDatosImpresionAlbaran\(string empresa, int numeroAlbaran\)', `
    'private DocumentoParaImprimir GenerarDatosImpresionAlbaran(CabPedidoVta pedido, string empresa, int numeroAlbaran)'

# Cambio 2: Modificar firma de GenerarDatosImpresionFactura
$content = $content -replace `
    'private DocumentoParaImprimir GenerarDatosImpresionFactura\(string empresa, string numeroFactura\)', `
    'private DocumentoParaImprimir GenerarDatosImpresionFactura(CabPedidoVta pedido, string empresa, string numeroFactura)'

# Cambio 3: Modificar contenido de GenerarDatosImpresionAlbaran
$oldAlbaranBody = @'
            var bytesPdf = gestorFacturas.FacturasEnPDF\(albaranes, papelConMembrete: false\);

            return new DocumentoParaImprimir
            \{
                BytesPDF = bytesPdf\.ReadAsByteArrayAsync\(\)\.Result,
                NumeroCopias = 1, // TODO: Configurar según reglas de negocio
                Bandeja = "Default" // TODO: Configurar según reglas de negocio
            \};
        \}

        /// <summary>
        /// Genera los datos de impresión para una factura
'@

$newAlbaranBody = @'
            var bytesPdf = gestorFacturas.FacturasEnPDF(albaranes, papelConMembrete: false);

            // Determinar tipo de ruta y obtener configuración de impresión
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

            // Si la ruta no está manejada por ningún tipo, no imprimir
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            string bandeja = tipoRuta != null ? tipoRuta.ObtenerBandeja() : "Default";

            return new DocumentoParaImprimir
            {
                BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
                NumeroCopias = numeroCopias,
                Bandeja = bandeja
            };
        }

        /// <summary>
        /// Genera los datos de impresión para una factura
'@

$content = $content -replace $oldAlbaranBody, $newAlbaranBody

# Cambio 4: Modificar contenido de GenerarDatosImpresionFactura
$oldFacturaBody = @'
            var bytesPdf = gestorFacturas.FacturasEnPDF\(facturas, papelConMembrete: false\);

            return new DocumentoParaImprimir
            \{
                BytesPDF = bytesPdf\.ReadAsByteArrayAsync\(\)\.Result,
                NumeroCopias = 1, // TODO: Configurar según reglas de negocio
                Bandeja = "Default" // TODO: Configurar según reglas de negocio
            \};
        \}

        /// <summary>
        /// Crea un DTO de albarán
'@

$newFacturaBody = @'
            var bytesPdf = gestorFacturas.FacturasEnPDF(facturas, papelConMembrete: false);

            // Determinar tipo de ruta y obtener configuración de impresión
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

            // Si la ruta no está manejada por ningún tipo, no imprimir
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            string bandeja = tipoRuta != null ? tipoRuta.ObtenerBandeja() : "Default";

            return new DocumentoParaImprimir
            {
                BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
                NumeroCopias = numeroCopias,
                Bandeja = bandeja
            };
        }

        /// <summary>
        /// Crea un DTO de albarán
'@

$content = $content -replace $oldFacturaBody, $newFacturaBody

# Cambio 5: Actualizar llamada en línea ~304
$content = $content -replace `
    'facturaCreada\.DatosImpresion = GenerarDatosImpresionFactura\(pedido\.Empresa, numeroFactura\);', `
    'facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido, pedido.Empresa, numeroFactura);'

# Cambio 6: Actualizar llamada en línea ~354
$content = $content -replace `
    'albaran\.DatosImpresion = GenerarDatosImpresionAlbaran\(pedido\.Empresa, numeroAlbaran\);', `
    'albaran.DatosImpresion = GenerarDatosImpresionAlbaran(pedido, pedido.Empresa, numeroAlbaran);'

# Guardar el archivo
$content | Set-Content $file -NoNewline

Write-Host "Cambios aplicados exitosamente!"
