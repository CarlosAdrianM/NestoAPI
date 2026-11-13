using NestoAPI.Infraestructure.Pagos.PagoAPlazos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos.PagoAPlazos
{
    /// <summary>NestoAPI#864: la tabla de solicitudes en memoria, con el mismo UPDATE condicional que la de verdad.</summary>
    internal class RepositorioPagoAPlazosEnMemoria : IRepositorioPagoAPlazos
    {
        public List<SolicitudPagoAPlazos> Filas { get; } = new List<SolicitudPagoAPlazos>();
        public bool HayTabla { get; set; } = true;

        public bool TablaDisponible() => HayTabla;

        public SolicitudPagoAPlazos Anadir(SolicitudPagoAPlazos solicitud)
        {
            if (solicitud.Id == 0)
            {
                solicitud.Id = Filas.Count == 0 ? 1 : Filas.Max(f => f.Id) + 1;
            }
            Filas.Add(solicitud);
            return solicitud;
        }

        private static SolicitudPagoAPlazos Copia(SolicitudPagoAPlazos s) => s == null ? null : new SolicitudPagoAPlazos
        {
            Id = s.Id, Empresa = s.Empresa, Pedido = s.Pedido, Cliente = s.Cliente, Contacto = s.Contacto, Proveedor = s.Proveedor,
            Referencia = s.Referencia, ReferenciaExterna = s.ReferenciaExterna, Importe = s.Importe, Canal = s.Canal, Estado = s.Estado,
            Pruebas = s.Pruebas, Usuario = s.Usuario, Correo = s.Correo, FechaCreacion = s.FechaCreacion, FechaAprobacion = s.FechaAprobacion,
            FechaLimiteCaptura = s.FechaLimiteCaptura, FechaCaptura = s.FechaCaptura, ImporteCapturado = s.ImporteCapturado,
            FechaAvisoPlazo = s.FechaAvisoPlazo, FechaCierre = s.FechaCierre, Motivo = s.Motivo, PrepagoId = s.PrepagoId
        };

        public Task<int> Crear(SolicitudPagoAPlazos solicitud)
        {
            SolicitudPagoAPlazos copia = Copia(solicitud);
            copia.Id = 0;
            return Task.FromResult(Anadir(copia).Id);
        }

        public Task GuardarReferencias(int id, string referencia, string referenciaExterna)
        {
            SolicitudPagoAPlazos fila = Filas.Single(f => f.Id == id);
            fila.Referencia = referencia ?? fila.Referencia;
            fila.ReferenciaExterna = referenciaExterna ?? fila.ReferenciaExterna;
            return Task.CompletedTask;
        }

        public Task<SolicitudPagoAPlazos> Leer(int id) => Task.FromResult(Copia(Filas.SingleOrDefault(f => f.Id == id)));

        public Task<SolicitudPagoAPlazos> LeerPorReferencia(string proveedor, string referencia)
            => Task.FromResult(Copia(Filas.SingleOrDefault(f => string.Equals(f.Proveedor, proveedor, StringComparison.OrdinalIgnoreCase)
                && f.Referencia == referencia)));

        public Task<List<SolicitudPagoAPlazos>> LeerDelPedido(string empresa, int pedido)
            => Task.FromResult(Filas.Where(f => f.Empresa?.Trim() == empresa?.Trim() && f.Pedido == pedido).OrderBy(f => f.Id).Select(Copia).ToList());

        public Task<List<SolicitudPagoAPlazos>> LeerVivas()
            => Task.FromResult(Filas.Where(f => EstadosPagoAPlazos.EsViva(f.Estado)).OrderBy(f => f.Id).Select(Copia).ToList());

        public Task<bool> CambiarEstado(int id, IEnumerable<string> desde, string a, DateTime ahora, string motivo = null, DateTime? fechaLimiteCaptura = null)
        {
            SolicitudPagoAPlazos fila = Filas.SingleOrDefault(f => f.Id == id);
            if (fila == null || !desde.Contains(fila.Estado))
            {
                return Task.FromResult(false);
            }
            fila.Estado = a;
            if (a == EstadosPagoAPlazos.APROBADO)
            {
                fila.FechaAprobacion = ahora;
            }
            fila.FechaLimiteCaptura = fechaLimiteCaptura ?? fila.FechaLimiteCaptura;
            if (a == EstadosPagoAPlazos.CANCELADO || a == EstadosPagoAPlazos.CADUCADO || a == EstadosPagoAPlazos.RECHAZADO)
            {
                fila.FechaCierre = ahora;
            }
            fila.Motivo = motivo ?? fila.Motivo;
            return Task.FromResult(true);
        }

        public Task<bool> GuardarCaptura(int id, DateTime fecha, decimal importe, string motivo = null)
        {
            SolicitudPagoAPlazos fila = Filas.SingleOrDefault(f => f.Id == id);
            if (fila == null || fila.Estado != EstadosPagoAPlazos.APROBADO)
            {
                return Task.FromResult(false);
            }
            fila.Estado = EstadosPagoAPlazos.CAPTURADO;
            fila.FechaCaptura = fecha;
            fila.ImporteCapturado = importe;
            fila.Motivo = motivo ?? fila.Motivo;
            return Task.FromResult(true);
        }

        public Task MarcarAvisoPlazo(int id, DateTime fecha)
        {
            Filas.Single(f => f.Id == id).FechaAvisoPlazo = fecha;
            return Task.CompletedTask;
        }

        public Task GuardarPrepago(int id, int prepagoId)
        {
            Filas.Single(f => f.Id == id).PrepagoId = prepagoId;
            return Task.CompletedTask;
        }
    }

    /// <summary>Un prepago apuntado en memoria.</summary>
    internal class PrepagoEnMemoria
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public decimal Importe { get; set; }
        public string Cuenta { get; set; }
        public string Concepto { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaCobro { get; set; }
        public bool Facturado { get; set; }
    }

    /// <summary>NestoAPI#864: los pedidos y sus prepagos en memoria.</summary>
    internal class PedidosPagoAPlazosEnMemoria : IPedidosPagoAPlazos
    {
        public Dictionary<int, PedidoPagoAPlazos> Pedidos { get; } = new Dictionary<int, PedidoPagoAPlazos>();
        public List<PrepagoEnMemoria> Prepagos { get; } = new List<PrepagoEnMemoria>();
        public bool FallaElPrepago { get; set; }

        public Task<PedidoPagoAPlazos> Leer(string empresa, int pedido)
        {
            if (!Pedidos.TryGetValue(pedido, out PedidoPagoAPlazos datos))
            {
                return Task.FromResult<PedidoPagoAPlazos>(null);
            }
            // Como la de verdad: los prepagos del pedido son los que hay AHORA
            datos.Prepagos = Prepagos.Where(p => p.Pedido == pedido).Sum(p => p.Importe);
            return Task.FromResult(datos);
        }

        public Task<PrepagoPagoAPlazos> AnadirPrepago(string empresa, int pedido, decimal importe, string cuenta, string concepto, string usuario,
            DateTime fechaCobro)
        {
            if (FallaElPrepago)
            {
                throw new InvalidOperationException("La base de datos no contesta");
            }
            if (!Pedidos.ContainsKey(pedido))
            {
                return Task.FromResult<PrepagoPagoAPlazos>(null);
            }
            PrepagoEnMemoria existente = Prepagos.SingleOrDefault(p => p.Pedido == pedido && p.Concepto == concepto);
            if (existente != null)
            {
                return Task.FromResult(new PrepagoPagoAPlazos { Id = existente.Id, Creado = false });
            }
            PrepagoEnMemoria nuevo = new PrepagoEnMemoria
            {
                Id = Prepagos.Count + 101, Empresa = empresa, Pedido = pedido, Importe = importe, Cuenta = cuenta, Concepto = concepto,
                Usuario = usuario, FechaCobro = fechaCobro.Date
            };
            Prepagos.Add(nuevo);
            return Task.FromResult(new PrepagoPagoAPlazos { Id = nuevo.Id, Creado = true });
        }

        public Task<bool> QuitarPrepago(int prepagoId)
        {
            PrepagoEnMemoria prepago = Prepagos.SingleOrDefault(p => p.Id == prepagoId);
            if (prepago == null || prepago.Facturado)
            {
                return Task.FromResult(false);
            }
            _ = Prepagos.Remove(prepago);
            return Task.FromResult(true);
        }

        public Task<bool> AjustarPrepago(int prepagoId, decimal importe)
        {
            PrepagoEnMemoria prepago = Prepagos.SingleOrDefault(p => p.Id == prepagoId);
            if (prepago == null || prepago.Facturado || prepago.Importe == importe)
            {
                return Task.FromResult(false);
            }
            prepago.Importe = importe;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// NestoAPI#864: una financiera INVENTADA, que no tiene nada de Aplázame: otro código, otro nombre, otros límites, otro
    /// plazo, otra cuenta, otra forma de notificar (texto plano «referencia|tipo|importe» con una cabecera «Secreto xyz») y de
    /// contestar. Si el núcleo funciona con ella, es que no depende de Aplázame.
    /// </summary>
    internal class FinancieraFalsa : IProveedorPagoAPlazos
    {
        public const string CODIGO = "Falsa";
        public const string SECRETO = "Secreto xyz";

        public bool Operativa { get; set; } = true;
        public decimal? Minimo { get; set; } = 50M;
        public decimal? Maximo { get; set; } = 3000M;
        public int DiasPlazo { get; set; } = 10;
        public bool FallaAlCrear { get; set; }
        public bool FallaAlCancelar { get; set; }
        public EstadoCapturaProveedor YaCapturado { get; set; }

        public List<PeticionSolicitudProveedor> Creadas { get; } = new List<PeticionSolicitudProveedor>();
        public List<KeyValuePair<string, decimal>> Capturas { get; } = new List<KeyValuePair<string, decimal>>();
        public List<string> Canceladas { get; } = new List<string>();
        public List<KeyValuePair<string, decimal>> Reembolsos { get; } = new List<KeyValuePair<string, decimal>>();

        public string Codigo => CODIGO;
        public string Nombre => "Financiera Falsa";

        public CondicionesProveedorPagoAPlazos Condiciones() => new CondicionesProveedorPagoAPlazos
        {
            Operativo = Operativa,
            ImporteMinimo = Minimo,
            ImporteMaximo = Maximo,
            DiasPlazoCaptura = DiasPlazo,
            CuentaPrepago = "57299999",
            PrefijoConceptoPrepago = "Falsa pedido"
        };

        public Task<string> CrearSolicitud(PeticionSolicitudProveedor peticion)
        {
            if (FallaAlCrear)
            {
                throw new InvalidOperationException("La financiera no contesta");
            }
            Creadas.Add(peticion);
            return Task.FromResult("EXT-" + peticion.Referencia);
        }

        public string UrlPago(ReferenciaSolicitudProveedor solicitud, string canal)
            => $"https://falsa.example/{canal}/{solicitud.ReferenciaExterna}";

        public NotificacionPagoAPlazos InterpretarNotificacion(string cabeceraAutorizacion, string cuerpo)
        {
            if (cabeceraAutorizacion != SECRETO)
            {
                return new NotificacionPagoAPlazos { Tipo = TipoNotificacionPagoAPlazos.NoAutorizada };
            }
            string[] partes = (cuerpo ?? string.Empty).Split('|');
            if (partes.Length < 2 || !Enum.TryParse(partes[1], out TipoNotificacionPagoAPlazos tipo))
            {
                return new NotificacionPagoAPlazos { Tipo = TipoNotificacionPagoAPlazos.Ilegible };
            }
            return new NotificacionPagoAPlazos
            {
                Tipo = tipo,
                Referencia = partes[0],
                Importe = partes.Length > 2 ? decimal.Parse(partes[2], System.Globalization.CultureInfo.InvariantCulture) : (decimal?)null
            };
        }

        public RespuestaNotificacionProveedor ResponderNotificacion(DecisionNotificacionPagoAPlazos decision)
            => new RespuestaNotificacionProveedor { CodigoHttp = 299, Cuerpo = decision.ToString(), TipoContenido = "text/plain" };

        public Task<EstadoCapturaProveedor> ConsultarCaptura(ReferenciaSolicitudProveedor solicitud)
            => Task.FromResult(YaCapturado ?? new EstadoCapturaProveedor { Capturado = 0, PendienteDeCapturar = solicitud.Importe });

        public Task Capturar(ReferenciaSolicitudProveedor solicitud, decimal importe)
        {
            Capturas.Add(new KeyValuePair<string, decimal>(solicitud.Referencia, importe));
            return Task.CompletedTask;
        }

        public Task Cancelar(ReferenciaSolicitudProveedor solicitud)
        {
            if (FallaAlCancelar)
            {
                throw new InvalidOperationException("La financiera no deja cancelar");
            }
            Canceladas.Add(solicitud.Referencia);
            return Task.CompletedTask;
        }

        public Task Reembolsar(ReferenciaSolicitudProveedor solicitud, decimal importe)
        {
            Reembolsos.Add(new KeyValuePair<string, decimal>(solicitud.Referencia, importe));
            return Task.CompletedTask;
        }
    }

    /// <summary>Los avisos, apuntados.</summary>
    internal class AvisosPagoAPlazosEspia : IAvisosPagoAPlazos
    {
        public List<string> Avisos { get; } = new List<string>();
        /// <summary>Los del resultado (aprobado / rechazado) que van al CLIENTE, aparte de los del vendedor.</summary>
        public List<string> AvisosCliente { get; } = new List<string>();
        public int Dispositivos { get; set; } = 1;
        public bool FallaLaPush { get; set; }
        public bool FallaElAvisoAlVendedor { get; set; }

        public Task<int> AprobadoAlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor)
        {
            if (FallaLaPush)
            {
                throw new InvalidOperationException("Firebase no contesta");
            }
            AvisosCliente.Add($"AprobadoCliente:{solicitud.Pedido}:{nombreProveedor}:{solicitud.Estado}");
            return Task.FromResult(Dispositivos);
        }

        public Task<int> RechazadoAlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor, bool despuesDeAprobar)
        {
            if (FallaLaPush)
            {
                throw new InvalidOperationException("Firebase no contesta");
            }
            AvisosCliente.Add($"RechazadoCliente:{solicitud.Pedido}:{nombreProveedor}:{solicitud.Estado}:{despuesDeAprobar}");
            return Task.FromResult(Dispositivos);
        }

        public Task<int> AlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor)
        {
            if (FallaLaPush)
            {
                throw new InvalidOperationException("Firebase no contesta");
            }
            Avisos.Add($"Cliente:{solicitud.Pedido}:{nombreProveedor}");
            return Task.FromResult(Dispositivos);
        }

        public Task Aprobado(SolicitudPagoAPlazos solicitud, string nombreProveedor, decimal faltaPorPagar, bool contraReembolso)
        {
            if (FallaElAvisoAlVendedor)
            {
                throw new InvalidOperationException("El buzón no contesta");
            }
            Avisos.Add($"Aprobado:{solicitud.Pedido}:{faltaPorPagar.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}:{contraReembolso}");
            return Task.CompletedTask;
        }

        public Task Rechazado(SolicitudPagoAPlazos solicitud, string nombreProveedor)
        {
            Avisos.Add($"Rechazado:{solicitud.Pedido}");
            return Task.CompletedTask;
        }

        public Task PlazoCerca(SolicitudPagoAPlazos solicitud, string nombreProveedor, int dias)
        {
            Avisos.Add($"PlazoCerca:{solicitud.Pedido}:{dias}");
            return Task.CompletedTask;
        }

        public Task Perdido(SolicitudPagoAPlazos solicitud, string nombreProveedor, string motivo)
        {
            Avisos.Add($"Perdido:{solicitud.Pedido}:{motivo}");
            return Task.CompletedTask;
        }
    }

    internal static class DatosPagoAPlazos
    {
        public const int PEDIDO = 930500;
        public const string CLIENTE = "15191";
        public static readonly DateTime AHORA = new DateTime(2026, 11, 13, 12, 0, 0);

        public static LineaPagoAPlazos Linea(decimal total, int estado = Constantes.EstadosLineaVenta.PENDIENTE, DateTime? entrega = null, bool producto = true)
            => new LineaPagoAPlazos
            {
                EsProducto = producto,
                Producto = producto ? "17404" : "62400003",
                Texto = producto ? "CREMA" : "PORTES",
                Cantidad = 1,
                BaseImponible = Math.Round(total / 1.21M, 2),
                Impuestos = 0.21M,
                Total = total,
                Estado = (short)estado,
                FechaEntrega = entrega ?? AHORA.Date.AddDays(1)
            };

        public static PedidoPagoAPlazos Pedido(decimal total = 302.50M, bool tieneApp = true, int estado = Constantes.EstadosLineaVenta.PENDIENTE)
            => new PedidoPagoAPlazos
            {
                Empresa = "1",
                Pedido = PEDIDO,
                Cliente = CLIENTE,
                Contacto = "0",
                NombreCliente = "Centro de Estética",
                Lineas = new List<LineaPagoAPlazos> { Linea(total, estado) },
                Correo = "cliente@example.com",
                Facturacion = new DireccionPagoAPlazos { Nombre = "Centro de Estética", Direccion = "Calle Mayor 1", CodigoPostal = "28110", Poblacion = "Algete", Provincia = "Madrid" },
                Envio = new DireccionPagoAPlazos { Nombre = "Centro de Estética", Direccion = "Calle Mayor 1", CodigoPostal = "28110", Poblacion = "Algete", Provincia = "Madrid" },
                ClienteTieneApp = tieneApp
            };

        public static ConfiguracionPagoAPlazos Configuracion(ModoPagoAPlazos modo = ModoPagoAPlazos.Activo, string proveedor = FinancieraFalsa.CODIGO)
            => new ConfiguracionPagoAPlazos { Modo = modo, Proveedor = proveedor };

        public static SolicitudPagoAPlazos Solicitud(string estado = EstadosPagoAPlazos.PENDIENTE, decimal importe = 302.50M, int id = 7,
            string canal = CanalesPagoAPlazos.APP)
            => new SolicitudPagoAPlazos
            {
                Id = id,
                Empresa = "1",
                Pedido = PEDIDO,
                Cliente = CLIENTE,
                Contacto = "0",
                Proveedor = FinancieraFalsa.CODIGO,
                Referencia = "PAP-" + id,
                ReferenciaExterna = "EXT-PAP-" + id,
                Importe = importe,
                Canal = canal,
                Estado = estado,
                Usuario = "NUEVAVISION\\Paloma",
                Correo = "cliente@example.com",
                FechaCreacion = AHORA.AddDays(-1)
            };
    }
}
