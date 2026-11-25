using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Facturas.Facturae
{
    /// <summary>NestoAPI#903: los códigos DIR3 del organismo (guardados en la ficha o pasados en la llamada) y sus nombres.</summary>
    public class DatosOrganismoFacturae
    {
        public string OficinaContable { get; set; }
        public string OrganoGestor { get; set; }
        public string UnidadTramitadora { get; set; }
        public string OrganoProponente { get; set; }
        public string NombreOficinaContable { get; set; }
        public string NombreOrganoGestor { get; set; }
        public string NombreUnidadTramitadora { get; set; }
        public string NombreOrganoProponente { get; set; }
    }

    /// <summary>NestoAPI#903: las referencias del organismo (todas TextMax20 en Facturae).</summary>
    public class ReferenciasFacturae
    {
        /// <summary>
        /// ReceiverTransactionReference: el «Su pedido / P.O.» que se pase o el de la factura. Vacío: cada línea lleva el número de
        /// NUESTRO pedido (Carlos 25/11/26: «si tenemos PO lo ponemos; si no, ponemos el nuestro»).
        /// </summary>
        public string Pedido { get; set; }
        /// <summary>De dónde sale <see cref="Pedido"/>: <see cref="ConstructorModeloFacturae.ORIGEN_PEDIDO_PARAMETRO"/> o
        /// <see cref="ConstructorModeloFacturae.ORIGEN_PEDIDO_FACTURA"/> (si no se dice, de la factura).</summary>
        public string OrigenPedido { get; set; }
        /// <summary>FileReference: el número de expediente.</summary>
        public string Expediente { get; set; }
        /// <summary>ReceiverContractReference: el número de contrato.</summary>
        public string Contrato { get; set; }
    }

    /// <summary>NestoAPI#903: la factura original de una rectificativa (de LinFacturaVtaRectificacion).</summary>
    public class FacturaOriginalFacturae
    {
        public string Serie { get; set; }
        /// <summary>Sin la serie.</summary>
        public string Numero { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#903: todo lo leído de una factura emitida, tal cual (lo prepara <see cref="ServicioFacturae"/>).</summary>
    public class DatosOrigenFacturae
    {
        /// <summary>La factura del PDF (<see cref="IGestorFacturas.LeerFactura"/>): direcciones, NIF, totales y vencimientos.</summary>
        public Factura Factura { get; set; }
        /// <summary>Las líneas de la factura (CabFacturaVta.LinPedidoVtas): llevan el tipo de IVA y recargo de cada una.</summary>
        public List<LinPedidoVta> Lineas { get; set; } = new List<LinPedidoVta>();
        public string NifEmpresa { get; set; }
        public bool EsRectificativa { get; set; }
        public List<FacturaOriginalFacturae> FacturasOriginales { get; set; } = new List<FacturaOriginalFacturae>();
        public DatosOrganismoFacturae Organismo { get; set; }
        public ReferenciasFacturae Referencias { get; set; }
    }

    /// <summary>NestoAPI#903: el modelo listo para escribir, o lo que falta (de usted) y los avisos que no impiden descargarlo.</summary>
    public class ResultadoModeloFacturae
    {
        public ModeloFacturae Modelo { get; set; }
        public List<string> Faltas { get; set; } = new List<string>();
        public List<string> Avisos { get; set; } = new List<string>();
        public bool Listo => Modelo != null && !Faltas.Any();
        /// <summary>El «Su pedido» que sale en las líneas (con varios pedidos nuestros, separados por coma), o null.</summary>
        public string SuPedido { get; set; }
        /// <summary>«Parametro», «Factura» o «NuestroPedido» (null si no sale ninguno).</summary>
        public string OrigenSuPedido { get; set; }
    }

    /// <summary>
    /// NestoAPI#903: de los datos leídos de una factura emitida al <see cref="ModeloFacturae"/>. Puro.
    /// <para>Importes: los de la factura real. Cabecera por (IVA, recargo) con la MISMA regla que el PDF y Verifactu
    /// (base = suma de bases; cuota = ROUND(suma de ImporteIVA, 2); recargo = ROUND(suma de PorcentajeRE × base, 2)). En la línea:
    /// TotalCost = Bruto (Cantidad × Precio, sin redondear), GrossAmount = su base imponible y DiscountAmount la diferencia (el
    /// descuento redondeado más el redondeo del Bruto a 2: así la suma de líneas es la base de la factura al céntimo). Las cuotas de
    /// línea van a 2 decimales y la línea mayor de cada tipo se lleva el céntimo que falte para sumar la de la cabecera.</para>
    /// </summary>
    public static class ConstructorModeloFacturae
    {
        public const string FALTAN_DIR3 = "Faltan los códigos DIR3 del organismo (oficina contable, órgano gestor y unidad tramitadora). Pídaselos al organismo y guárdelos en la ficha del cliente.";
        public const string ROL_OFICINA_CONTABLE = "01";
        public const string ROL_ORGANO_GESTOR = "02";
        public const string ROL_UNIDAD_TRAMITADORA = "03";
        public const string ROL_ORGANO_PROPONENTE = "04";
        public const int LONGITUD_REFERENCIA = 20;
        public const string ORIGEN_PEDIDO_PARAMETRO = "Parametro";
        public const string ORIGEN_PEDIDO_FACTURA = "Factura";
        public const string ORIGEN_PEDIDO_NUESTRO = "NuestroPedido";

        public static ResultadoModeloFacturae Construir(DatosOrigenFacturae datos)
        {
            var resultado = new ResultadoModeloFacturae();
            Factura factura = datos?.Factura;
            if (factura == null)
            {
                resultado.Faltas.Add("No se ha podido leer la factura.");
                return resultado;
            }

            // Emisor: los datos de la empresa que pinta el PDF (dirección «Empresa») y el NIF de la ficha de la empresa
            DireccionFactura direccionEmpresa = factura.Direcciones?.FirstOrDefault(d => d.Tipo == "Empresa");
            var emisor = new ParteFacturae
            {
                Nif = NormalizarNif(datos.NifEmpresa),
                TipoPersona = "J",
                Nombre = direccionEmpresa?.Nombre?.Trim(),
                Direccion = Direccion(direccionEmpresa)
            };
            if (string.IsNullOrEmpty(emisor.Nif))
            {
                resultado.Faltas.Add("La empresa emisora no tiene NIF en su ficha.");
            }
            ComprobarDireccion("La empresa emisora", emisor, resultado);

            // Receptor: los datos fiscales de la factura (los guardados al emitirla) y su NIF
            DireccionFactura direccionFiscal = factura.Direcciones?.FirstOrDefault(d => d.Tipo == "Fiscal");
            var receptor = new ParteFacturae
            {
                Nif = NormalizarNif(factura.Nif),
                TipoPersona = TipoPersona(NormalizarNif(factura.Nif)),
                Nombre = direccionFiscal?.Nombre?.Trim(),
                Direccion = Direccion(direccionFiscal)
            };
            if (string.IsNullOrEmpty(receptor.Nif))
            {
                resultado.Faltas.Add($"El cliente {factura.Cliente} no tiene NIF en la factura. Sin NIF no se puede presentar en FACe.");
            }
            else if (receptor.TipoPersona == "F")
            {
                // Un organismo es siempre persona jurídica: el fichero va con LegalEntity
                resultado.Avisos.Add($"El NIF {receptor.Nif} parece de una persona física; FACe es para organismos públicos. Compruebe que es la factura correcta.");
                receptor.TipoPersona = "J";
            }
            ComprobarDireccion("El cliente", receptor, resultado);

            // Centros DIR3
            List<CentroAdministrativoFacturae> centros = Centros(datos.Organismo, receptor.Direccion, resultado);

            // Referencias
            ReferenciasFacturae referencias = datos.Referencias ?? new ReferenciasFacturae();
            // Carlos 25/11/26: «si tenemos PO lo ponemos; si no, ponemos el nuestro». Prioridad: el que se pasa > el guardado en la
            // factura > el número de NUESTRO pedido de cada línea (una factura puede agrupar varios: cada línea lleva el suyo).
            bool hayPedidoDelOrganismo = !string.IsNullOrWhiteSpace(referencias.Pedido);
            string pedido = Referencia(referencias.Pedido, "El «Su pedido» (referencia del pedido del organismo)", resultado);
            string expediente = Referencia(referencias.Expediente, "El expediente", resultado);
            string contrato = Referencia(referencias.Contrato, "El contrato", resultado);
            if (pedido != null)
            {
                resultado.SuPedido = pedido;
                resultado.OrigenSuPedido = string.IsNullOrWhiteSpace(referencias.OrigenPedido) ? ORIGEN_PEDIDO_FACTURA : referencias.OrigenPedido;
            }

            // Líneas
            List<LineaFacturae> lineas = new List<LineaFacturae>();
            List<LinPedidoVta> origen = (datos.Lineas ?? new List<LinPedidoVta>()).OrderBy(l => l.Nº_Orden).ToList();
            var porLinea = new Dictionary<LineaFacturae, LinPedidoVta>();
            foreach (LinPedidoVta l in origen)
            {
                decimal cantidad = l.Cantidad ?? 0;
                if (l.Base_Imponible == 0 && l.Bruto == 0)
                {
                    continue; // líneas de texto (sin importe)
                }
                decimal precio = l.Precio ?? 0;
                if (cantidad == 0)
                {
                    // Sin cantidad pero con importe: una unidad del importe, para que TotalCost = Quantity × UnitPrice
                    cantidad = 1;
                    precio = l.Bruto;
                    resultado.Avisos.Add($"La línea {l.Nº_Orden} ({l.Texto?.Trim()}) no tiene cantidad: va como 1 unidad de {GeneradorFacturae.Dos(l.Bruto)} €.");
                }
                decimal brutoCentimos = RoundingHelper.DosDecimalesRound(l.Bruto);
                decimal descuento = brutoCentimos - l.Base_Imponible;
                var linea = new LineaFacturae
                {
                    ReferenciaPedido = hayPedidoDelOrganismo ? pedido : NuestroPedido(l),
                    Expediente = expediente,
                    Contrato = contrato,
                    Albaran = l.Nº_Albarán.HasValue && l.Nº_Albarán.Value != 0 ? l.Nº_Albarán.Value.ToString() : null,
                    FechaAlbaran = l.Nº_Albarán.HasValue && l.Nº_Albarán.Value != 0 ? l.Fecha_Albarán?.Date : null,
                    Descripcion = string.IsNullOrWhiteSpace(l.Texto) ? l.Producto?.Trim() : l.Texto.Trim(),
                    Cantidad = cantidad,
                    PrecioUnitario = precio,
                    CosteTotal = l.Bruto,
                    ImporteDescuento = descuento,
                    PorcentajeDescuento = descuento != 0 && l.SumaDescuentos != 0 ? l.SumaDescuentos * 100M : (decimal?)null,
                    MotivoDescuento = l.DescuentoPP != 0 ? "Descuento comercial y por pronto pago" : "Descuento",
                    ImporteRedondeo = brutoCentimos - l.Bruto,
                    ImporteBruto = l.Base_Imponible,
                    TipoIva = l.PorcentajeIVA,
                    CuotaIva = RoundingHelper.DosDecimalesRound(l.ImporteIVA),
                    TipoRecargo = l.PorcentajeRE * 100M,
                    CuotaRecargo = RoundingHelper.DosDecimalesRound(l.PorcentajeRE * l.Base_Imponible),
                    CodigoArticulo = l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO ? l.Producto?.Trim() : null
                };
                lineas.Add(linea);
                porLinea[linea] = l;
            }
            if (!lineas.Any())
            {
                resultado.Faltas.Add("La factura no tiene líneas con importe.");
            }

            if (!hayPedidoDelOrganismo)
            {
                List<string> nuestros = lineas.Select(x => x.ReferenciaPedido).Where(p => p != null).Distinct().ToList();
                if (nuestros.Any())
                {
                    resultado.SuPedido = string.Join(", ", nuestros);
                    resultado.OrigenSuPedido = ORIGEN_PEDIDO_NUESTRO;
                    resultado.Avisos.Add(nuestros.Count == 1
                        ? $"No consta el pedido del organismo: se pone nuestro número de pedido ({nuestros[0]}) como referencia."
                        : $"No consta el pedido del organismo: en cada línea se pone nuestro número de pedido ({resultado.SuPedido}) como referencia.");
                }
            }
            if (resultado.SuPedido == null && expediente == null && contrato == null)
            {
                resultado.Avisos.Add("La factura no lleva «Su pedido», expediente ni contrato del organismo. Muchos organismos lo exigen: si se lo han dado, páselo antes de descargar.");
            }

            // Impuestos de la cabecera: la regla del PDF y de Verifactu
            List<ImpuestoFacturae> impuestos = new List<ImpuestoFacturae>();
            foreach (var grupo in origen.GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE }))
            {
                var impuesto = new ImpuestoFacturae
                {
                    TipoIva = grupo.Key.PorcentajeIVA,
                    Base = grupo.Sum(l => l.Base_Imponible),
                    Cuota = RoundingHelper.DosDecimalesRound(grupo.Sum(l => l.ImporteIVA)),
                    TipoRecargo = grupo.Key.PorcentajeRE * 100M,
                    CuotaRecargo = RoundingHelper.DosDecimalesRound(grupo.Sum(l => l.PorcentajeRE * l.Base_Imponible))
                };
                List<LineaFacturae> delGrupo = lineas.Where(x => porLinea[x].PorcentajeIVA == grupo.Key.PorcentajeIVA && porLinea[x].PorcentajeRE == grupo.Key.PorcentajeRE).ToList();
                if (!delGrupo.Any())
                {
                    continue; // solo líneas de texto con ese tipo
                }
                AjustarCentimo(delGrupo, impuesto.Cuota - delGrupo.Sum(x => x.CuotaIva), (x, d) => x.CuotaIva += d);
                AjustarCentimo(delGrupo, impuesto.CuotaRecargo - delGrupo.Sum(x => x.CuotaRecargo), (x, d) => x.CuotaRecargo += d);
                impuestos.Add(impuesto);
            }

            var modelo = new ModeloFacturae
            {
                Emisor = emisor,
                Receptor = receptor,
                Centros = centros,
                Serie = factura.Serie?.Trim(),
                Numero = Verifactu.MapeadorFacturaVerifactu.NumeroSinSerie(factura.NumeroFactura, factura.Serie),
                Fecha = factura.Fecha.Date,
                Lineas = lineas,
                Impuestos = impuestos
            };

            // Rectificativa
            if (datos.EsRectificativa)
            {
                FacturaOriginalFacturae original = datos.FacturasOriginales?.FirstOrDefault();
                if (original == null)
                {
                    resultado.Faltas.Add($"La rectificativa {factura.NumeroFactura} no tiene vinculada la factura que rectifica. Sin ella no se puede presentar en FACe.");
                }
                else
                {
                    modelo.Rectificativa = new RectificativaFacturae
                    {
                        NumeroOriginal = original.Numero,
                        SerieOriginal = original.Serie,
                        FechaOriginal = original.Fecha.Date
                    };
                    if (datos.FacturasOriginales.Count > 1)
                    {
                        string otras = string.Join(", ", datos.FacturasOriginales.Skip(1).Select(o => o.Serie + o.Numero));
                        modelo.Rectificativa.MotivoAdicional = ModeloFacturae.Recortar($"Rectifica también: {otras}", 2500);
                        resultado.Avisos.Add($"La rectificativa rectifica varias facturas; Facturae solo admite una: va {original.Serie}{original.Numero} y las demás ({otras}) en el texto adicional.");
                    }
                }
            }

            // Totales: tienen que ser los de la factura
            TotalesFacturae totales = GeneradorFacturae.Totales(modelo);
            decimal totalFactura = RoundingHelper.DosDecimalesRound(factura.ImporteTotal);
            if (totales.TotalFactura != totalFactura)
            {
                resultado.Faltas.Add($"El total del fichero ({GeneradorFacturae.Dos(totales.TotalFactura)} €) no cuadra con el de la factura ({GeneradorFacturae.Dos(totalFactura)} €). Avise a Informática.");
            }

            // Vencimientos
            modelo.Vencimientos = Vencimientos(factura.Vencimientos, resultado);
            decimal sumaVencimientos = modelo.Vencimientos.Sum(v => v.Importe);
            if (!modelo.Vencimientos.Any())
            {
                resultado.Faltas.Add("La factura no tiene vencimientos.");
            }
            else if (sumaVencimientos != totales.TotalFactura)
            {
                resultado.Faltas.Add($"Los vencimientos suman {GeneradorFacturae.Dos(sumaVencimientos)} € y la factura {GeneradorFacturae.Dos(totales.TotalFactura)} €. Revise los vencimientos de la factura.");
            }

            resultado.Modelo = modelo;
            return resultado;
        }

        private static void AjustarCentimo(List<LineaFacturae> lineas, decimal diferencia, Action<LineaFacturae, decimal> ajustar)
        {
            if (diferencia == 0 || !lineas.Any())
            {
                return;
            }
            LineaFacturae mayor = lineas.OrderByDescending(l => Math.Abs(l.ImporteBruto)).First();
            ajustar(mayor, diferencia);
        }

        private static List<CentroAdministrativoFacturae> Centros(DatosOrganismoFacturae organismo, DireccionFacturae direccion, ResultadoModeloFacturae resultado)
        {
            var centros = new List<CentroAdministrativoFacturae>();
            organismo = organismo ?? new DatosOrganismoFacturae();
            if (string.IsNullOrWhiteSpace(organismo.OficinaContable) || string.IsNullOrWhiteSpace(organismo.OrganoGestor)
                || string.IsNullOrWhiteSpace(organismo.UnidadTramitadora))
            {
                resultado.Faltas.Add(FALTAN_DIR3);
            }
            Centro(centros, organismo.OficinaContable, ROL_OFICINA_CONTABLE, organismo.NombreOficinaContable, "Oficina contable", direccion, resultado);
            Centro(centros, organismo.OrganoGestor, ROL_ORGANO_GESTOR, organismo.NombreOrganoGestor, "Órgano gestor", direccion, resultado);
            Centro(centros, organismo.UnidadTramitadora, ROL_UNIDAD_TRAMITADORA, organismo.NombreUnidadTramitadora, "Unidad tramitadora", direccion, resultado);
            Centro(centros, organismo.OrganoProponente, ROL_ORGANO_PROPONENTE, organismo.NombreOrganoProponente, "Órgano proponente", direccion, resultado);
            return centros;
        }

        private static void Centro(List<CentroAdministrativoFacturae> centros, string codigo, string rol, string nombre, string descripcion,
            DireccionFacturae direccion, ResultadoModeloFacturae resultado)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return;
            }
            string motivo = ReglasDir3.Validar(codigo, descripcion, out string limpio);
            if (motivo != null)
            {
                resultado.Faltas.Add(motivo);
                return;
            }
            centros.Add(new CentroAdministrativoFacturae
            {
                Codigo = limpio,
                Rol = rol,
                Nombre = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim(),
                Direccion = direccion,
                Descripcion = descripcion
            });
        }

        /// <summary>El número de nuestro pedido de la línea (LinPedidoVta.Número, como mucho 20 caracteres), o null si no tiene.</summary>
        internal static string NuestroPedido(LinPedidoVta linea)
        {
            if (linea == null || linea.Número <= 0)
            {
                return null;
            }
            string numero = linea.Número.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return numero.Length > LONGITUD_REFERENCIA ? null : numero;
        }

        private static string Referencia(string valor, string que, ResultadoModeloFacturae resultado)
        {
            string limpio = valor?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (limpio.Length > LONGITUD_REFERENCIA)
            {
                resultado.Faltas.Add($"{que} «{limpio}» tiene {limpio.Length} caracteres y Facturae admite como mucho {LONGITUD_REFERENCIA}.");
                return null;
            }
            return limpio;
        }

        /// <summary>NestoAPI#903: la forma de pago de Nesto → PaymentMeans de Facturae. Null = sin equivalencia clara.</summary>
        internal static string MedioPago(string formaPago)
        {
            switch (formaPago?.Trim().ToUpperInvariant())
            {
                case Constantes.FormasPago.TRANSFERENCIA:
                case "CNF": // confirming: nos paga el banco del cliente por transferencia (el PDF le pone nuestro IBAN, como a TRN)
                    return "04";
                case Constantes.FormasPago.RECIBO_BANCARIO:
                    return "02";
                case Constantes.FormasPago.EFECTIVO:
                    return "01";
                case Constantes.FormasPago.TARJETA:
                    return "19";
                default:
                    return null;
            }
        }

        private static List<VencimientoFacturae> Vencimientos(List<VencimientoFactura> vencimientos, ResultadoModeloFacturae resultado)
        {
            var lista = new List<VencimientoFacturae>();
            foreach (VencimientoFactura v in (vencimientos ?? new List<VencimientoFactura>()).OrderBy(v => v.Vencimiento))
            {
                string forma = v.FormaPago?.Trim().ToUpperInvariant();
                string medio = MedioPago(forma);
                if (medio == null)
                {
                    resultado.Faltas.Add($"La forma de pago «{forma}» del vencimiento del {v.Vencimiento:dd/MM/yyyy} no tiene equivalencia en Facturae. Cámbiela por transferencia (TRN) o avise a Informática.");
                    continue;
                }
                var vencimiento = new VencimientoFacturae
                {
                    Fecha = v.Vencimiento.Date,
                    Importe = RoundingHelper.DosDecimalesRound(v.Importe),
                    MedioPago = medio,
                    FormaPagoNesto = forma
                };
                if (medio == "04")
                {
                    vencimiento.Iban = PrimerIban(v.Iban, out int cuantas);
                    if (vencimiento.Iban == null)
                    {
                        resultado.Faltas.Add("La empresa no tiene ninguna cuenta bancaria con IBAN válido para cobrar la transferencia.");
                    }
                    else if (cuantas > 1 && !resultado.Avisos.Any(a => a.StartsWith("La empresa tiene")))
                    {
                        resultado.Avisos.Add($"La empresa tiene {cuantas} cuentas bancarias; en el fichero va la primera ({vencimiento.Iban}).");
                    }
                }
                else
                {
                    resultado.Avisos.Add($"El vencimiento del {v.Vencimiento:dd/MM/yyyy} es de forma de pago {forma}; las administraciones suelen pagar por transferencia.");
                }
                lista.Add(vencimiento);
            }
            return lista;
        }

        /// <summary>El primer IBAN válido de la lista que pone el PDF (una cuenta por línea), sin espacios.</summary>
        internal static string PrimerIban(string ibans, out int cuantas)
        {
            List<string> validos = new List<string>();
            foreach (string linea in (ibans ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string compacto = Regex.Replace(linea, "[^0-9A-Za-z]", string.Empty).ToUpperInvariant();
                if (compacto.Length < 15 || !char.IsLetter(compacto[0]))
                {
                    continue;
                }
                try
                {
                    validos.Add(new Models.Clientes.Iban(compacto).Codigo);
                }
                catch (Exception)
                {
                    // IBAN incorrecto: no vale
                }
            }
            cuantas = validos.Count;
            return validos.FirstOrDefault();
        }

        private static DireccionFacturae Direccion(DireccionFactura d)
        {
            if (d == null)
            {
                return null;
            }
            string direccion = string.Join(", ", (d.Direccion ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length > 0));
            return new DireccionFacturae
            {
                Direccion = direccion,
                CodigoPostal = d.CodigoPostal?.Trim(),
                Poblacion = d.Poblacion?.Trim(),
                Provincia = d.Provincia?.Trim()
            };
        }

        private static void ComprobarDireccion(string quien, ParteFacturae parte, ResultadoModeloFacturae resultado)
        {
            if (string.IsNullOrWhiteSpace(parte.Nombre))
            {
                resultado.Faltas.Add($"{quien} no tiene nombre fiscal.");
            }
            DireccionFacturae d = parte.Direccion;
            if (d == null || string.IsNullOrWhiteSpace(d.Direccion) || string.IsNullOrWhiteSpace(d.Poblacion) || string.IsNullOrWhiteSpace(d.Provincia))
            {
                resultado.Faltas.Add($"{quien} no tiene la dirección fiscal completa (dirección, población y provincia).");
            }
            if (d == null || !Regex.IsMatch(d.CodigoPostal ?? string.Empty, "^[0-9]{5}$"))
            {
                resultado.Faltas.Add($"{quien} no tiene un código postal español de 5 cifras («{d?.CodigoPostal}»).");
            }
        }

        /// <summary>El NIF sin «ES» delante, sin espacios, puntos ni guiones y en mayúsculas.</summary>
        internal static string NormalizarNif(string nif)
        {
            string limpio = Regex.Replace(nif ?? string.Empty, "[^0-9A-Za-z]", string.Empty).ToUpperInvariant();
            if (limpio.Length > 9 && limpio.StartsWith("ES"))
            {
                limpio = limpio.Substring(2);
            }
            return limpio.Length == 0 ? null : limpio;
        }

        /// <summary>F si el NIF es de persona física (DNI o NIE: empieza por cifra o por K, L, M, X, Y, Z); J en otro caso.</summary>
        internal static string TipoPersona(string nif)
        {
            if (string.IsNullOrEmpty(nif))
            {
                return "J";
            }
            char primera = nif[0];
            return char.IsDigit(primera) || "KLMXYZ".IndexOf(primera) >= 0 ? "F" : "J";
        }
    }
}
