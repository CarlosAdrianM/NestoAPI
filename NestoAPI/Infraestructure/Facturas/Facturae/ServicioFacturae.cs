using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas.Facturae
{
    /// <summary>NestoAPI#903: lo que se puede pasar en la llamada (gana sobre lo guardado).</summary>
    public class PeticionFacturae
    {
        public string OficinaContable { get; set; }
        public string OrganoGestor { get; set; }
        public string UnidadTramitadora { get; set; }
        public string OrganoProponente { get; set; }
        public string SuPedido { get; set; }
        public string Expediente { get; set; }
        public string Contrato { get; set; }
    }

    /// <summary>NestoAPI#903: el resultado de preparar el fichero.</summary>
    public class PreparacionFacturae
    {
        /// <summary>La factura no existe (404).</summary>
        public bool NoExiste { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>De dónde salen los DIR3: «Ficha», «Llamada», «Ficha y llamada» o null (ninguno).</summary>
        public string OrigenDir3 { get; set; }
        public bool TablaCreada { get; set; }
        public ResultadoModeloFacturae Resultado { get; set; }
    }

    /// <summary>
    /// NestoAPI#903: reúne lo que ya leen el PDF y Verifactu de una factura emitida (solo LECTURA) y lo pasa a
    /// <see cref="ConstructorModeloFacturae"/>: la factura del PDF (<see cref="IGestorFacturas.LeerFactura"/>: datos fiscales
    /// guardados, NIF, totales, vencimientos e IBAN), sus líneas (CabFacturaVta.LinPedidoVtas), el NIF de la empresa
    /// (<see cref="IServicioFacturas.CargarEmpresa"/>), las facturas que rectifica (las vinculaciones de Verifactu) y los DIR3 del
    /// cliente (<see cref="IRepositorioClientesFacturaElectronica"/>).
    /// </summary>
    public class ServicioFacturae
    {
        private readonly IGestorFacturas gestor;
        private readonly IServicioFacturas servicio;
        private readonly IRepositorioClientesFacturaElectronica repositorio;
        private readonly Func<string, string, Task<List<VerifactuFacturaRectificada>>> leerRectificadas;

        public ServicioFacturae(IGestorFacturas gestor, IServicioFacturas servicio, IRepositorioClientesFacturaElectronica repositorio,
            Func<string, string, Task<List<VerifactuFacturaRectificada>>> leerRectificadas)
        {
            this.gestor = gestor;
            this.servicio = servicio;
            this.repositorio = repositorio;
            this.leerRectificadas = leerRectificadas;
        }

        public async Task<PreparacionFacturae> Preparar(string empresa, string numeroFactura, PeticionFacturae peticion)
        {
            peticion = peticion ?? new PeticionFacturae();
            string laEmpresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string numero = numeroFactura?.Trim();
            CabFacturaVta cabecera = string.IsNullOrEmpty(numero) ? null : servicio.CargarCabFactura(laEmpresa, numero);
            if (cabecera == null)
            {
                return new PreparacionFacturae { NoExiste = true };
            }

            var preparacion = new PreparacionFacturae
            {
                Cliente = cabecera.Nº_Cliente?.Trim(),
                Contacto = cabecera.Contacto?.Trim()
            };

            Factura factura;
            try
            {
                factura = gestor.LeerFactura(laEmpresa, numero);
            }
            catch (Exceptions.NestoBusinessException ex) when ((int)ex.StatusCode == 404)
            {
                return new PreparacionFacturae { NoExiste = true };
            }
            catch (Exception ex)
            {
                // P. ej. «No cuadran los vencimientos con el total de la factura»: el PDF tampoco sale
                var fallo = new ResultadoModeloFacturae();
                fallo.Faltas.Add($"No se ha podido leer la factura {numero}: {ex.Message}");
                preparacion.Resultado = fallo;
                return preparacion;
            }

            // DIR3: lo guardado del cliente (los clientes están siempre en la empresa 1) y, encima, lo que venga en la llamada
            LecturaClienteFacturaElectronica lectura = await repositorio.Leer(Constantes.Empresas.EMPRESA_POR_DEFECTO, preparacion.Cliente, preparacion.Contacto).ConfigureAwait(false);
            preparacion.TablaCreada = lectura?.HayTabla == true;
            DatosOrganismoFacturae guardado = lectura?.Fila;
            DatosOrganismoFacturae organismo = Combinar(guardado, peticion);
            bool hayLlamada = new[] { peticion.OficinaContable, peticion.OrganoGestor, peticion.UnidadTramitadora, peticion.OrganoProponente }
                .Any(c => !string.IsNullOrWhiteSpace(c));
            preparacion.OrigenDir3 = guardado != null && hayLlamada ? "Ficha y llamada" : guardado != null ? "Ficha" : hayLlamada ? "Llamada" : null;

            bool esRectificativa = RegistroSeriesVerifactu.ObtenerSerie(cabecera.Serie)?.EsRectificativa == true;
            var originales = new List<FacturaOriginalFacturae>();
            if (esRectificativa && leerRectificadas != null)
            {
                List<VerifactuFacturaRectificada> rectificadas = await leerRectificadas(laEmpresa, numero).ConfigureAwait(false);
                originales = (rectificadas ?? new List<VerifactuFacturaRectificada>())
                    .Select(r => new FacturaOriginalFacturae { Serie = r.Serie, Numero = r.Numero, Fecha = r.FechaExpedicion })
                    .ToList();
            }

            var datos = new DatosOrigenFacturae
            {
                Factura = factura,
                Lineas = cabecera.LinPedidoVtas?.ToList() ?? new List<LinPedidoVta>(),
                NifEmpresa = servicio.CargarEmpresa(laEmpresa)?.NIF,
                EsRectificativa = esRectificativa,
                FacturasOriginales = originales,
                Organismo = organismo,
                Referencias = new ReferenciasFacturae
                {
                    // Parámetro > el guardado en la factura > (en el constructor) nuestro número de pedido de cada línea
                    Pedido = string.IsNullOrWhiteSpace(peticion.SuPedido) ? factura.SuPedido : peticion.SuPedido,
                    OrigenPedido = !string.IsNullOrWhiteSpace(peticion.SuPedido) ? ConstructorModeloFacturae.ORIGEN_PEDIDO_PARAMETRO
                        : !string.IsNullOrWhiteSpace(factura.SuPedido) ? ConstructorModeloFacturae.ORIGEN_PEDIDO_FACTURA : null,
                    Expediente = peticion.Expediente,
                    Contrato = peticion.Contrato
                }
            };
            preparacion.Resultado = ConstructorModeloFacturae.Construir(datos);
            if (!preparacion.TablaCreada)
            {
                preparacion.Resultado.Avisos.Add("Todavía no existe la tabla de los DIR3 de los clientes (Scripts/Issue903_ClientesFacturaElectronica.sql): los códigos solo pueden venir en la llamada.");
            }
            return preparacion;
        }

        /// <summary>Código a código, el de la llamada si viene; si no, el guardado. Los nombres, de lo guardado si el código es el mismo.</summary>
        internal static DatosOrganismoFacturae Combinar(DatosOrganismoFacturae guardado, PeticionFacturae peticion)
        {
            guardado = guardado ?? new DatosOrganismoFacturae();
            string Elegir(string llamada, string ficha) => string.IsNullOrWhiteSpace(llamada) ? ficha?.Trim() : llamada.Trim();
            string Nombre(string llamada, string ficha, string nombreFicha) =>
                string.IsNullOrWhiteSpace(llamada) || string.Equals(llamada.Trim(), ficha?.Trim(), StringComparison.OrdinalIgnoreCase) ? nombreFicha : null;
            return new DatosOrganismoFacturae
            {
                OficinaContable = Elegir(peticion.OficinaContable, guardado.OficinaContable),
                OrganoGestor = Elegir(peticion.OrganoGestor, guardado.OrganoGestor),
                UnidadTramitadora = Elegir(peticion.UnidadTramitadora, guardado.UnidadTramitadora),
                OrganoProponente = Elegir(peticion.OrganoProponente, guardado.OrganoProponente),
                NombreOficinaContable = Nombre(peticion.OficinaContable, guardado.OficinaContable, guardado.NombreOficinaContable),
                NombreOrganoGestor = Nombre(peticion.OrganoGestor, guardado.OrganoGestor, guardado.NombreOrganoGestor),
                NombreUnidadTramitadora = Nombre(peticion.UnidadTramitadora, guardado.UnidadTramitadora, guardado.NombreUnidadTramitadora),
                NombreOrganoProponente = Nombre(peticion.OrganoProponente, guardado.OrganoProponente, guardado.NombreOrganoProponente)
            };
        }
    }
}
