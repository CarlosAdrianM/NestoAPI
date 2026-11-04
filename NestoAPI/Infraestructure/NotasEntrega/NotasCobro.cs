using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#786: lo que hace falta de la ficha del cliente/contacto para la cabecera de la nota y su parada.</summary>
    public class FichaNotaCobro
    {
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>El campo Teléfono de la ficha (todos, separados por «/»).</summary>
        public string Telefonos { get; set; }
        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public string Iva { get; set; }
        public string ContactoCobro { get; set; }
    }

    /// <summary>NestoAPI#786: lo guardado de una nota de cobro (NotasEntregaCabecera.ImporteCobro y NotasEntregaCobroEfectos).</summary>
    public class DatosNotaCobro
    {
        public int Pedido { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Los Nº Orden de ExtractoCliente, en el orden en que se liquidan.</summary>
        public List<int> Efectos { get; set; } = new List<int>();
        /// <summary>NestoAPI#786: el repartidor asignado (NotasEntregaCabecera.Repartidor, sin dominio); null sin Scripts/Issue786b.</summary>
        public string Repartidor { get; set; }
    }

    public interface IRepositorioNotasCobro
    {
        /// <summary>¿Se ha lanzado Scripts/Issue786_NotasCobro.sql (tabla NotasEntregaCobroEfectos y columna ImporteCobro)?</summary>
        bool ScriptAplicado();
        /// <summary>NestoAPI#786: ¿Se ha lanzado Scripts/Issue786b_NotaCobroRepartidor.sql (columna NotasEntregaCabecera.Repartidor)?</summary>
        bool ScriptRepartidorAplicado();
        /// <summary>La ficha del cliente/contacto, o null si no existe.</summary>
        Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto);
        /// <summary>Esos efectos de ExtractoCliente (en cualquier empresa; los que no existen no vienen).</summary>
        Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids);
        /// <summary>Las notas de cobro de ese cliente/contacto cuya parada sigue en el reparto (Tramitado o Incidentado).</summary>
        Task<List<int>> LeerNotasEnReparto(string empresa, string cliente, string contacto);
        /// <summary>La empresa de la agencia 14 (AgenciasTransporte.Empresa), o null.</summary>
        Task<string> LeerEmpresaAgencia();
        int ReservarNumeroPedido();
        void DevolverNumeroPedido(int numero);
        /// <summary>
        /// En UNA transacción: la cabecera del pedido (sin líneas), sus vendedores por grupo, la fila de Modificaciones, la de
        /// NotasEntregaCabecera (Tipo Cobro, Bultos 0, ImporteCobro), sus efectos y la parada (el envío de la agencia 14 ya tramitado,
        /// con su albarán RP{envío} y la historia). Devuelve el número del envío.
        /// </summary>
        Task<int> Guardar(CabPedidoVta cabecera, FilaNotaEntregaCabecera fila, decimal importe, IReadOnlyList<int> efectos,
            EnviosAgencia envio, string historia, Modificacion modificacion, string repartidor);
        /// <summary>El importe, el repartidor y los efectos de esas notas de cobro (las que no lo son, o sin el script, no vienen).</summary>
        Task<Dictionary<int, DatosNotaCobro>> LeerDatos(string empresa, IReadOnlyCollection<int> pedidos);
    }

    /// <summary>
    /// NestoAPI#786 (correo del 929609, 03/11/26): la nota de solo COBRO. Se vuelve a casa del cliente solo a cobrar (un envío que salió
    /// sin reembolso, una deuda): antes había que hacer una nota de entrega de texto, que pasaba por picking y packing (con caja y foto)
    /// y, en la parada, «Cobrar deuda» (F7). Ahora:
    /// <list type="bullet">
    /// <item>Un pedido con NotaEntrega = 1 SIN LÍNEAS (no entra en picking, packing ni Facturar rutas) y su NotasEntregaCabecera con
    /// Tipo «Cobro», Bultos 0, el importe y los efectos (NotasEntregaCobroEfectos).</item>
    /// <item>Al crearla, su parada: un envío de la agencia 14 ya tramitado (como las recogidas, <see cref="ServicioParadasNotaEntrega"/>),
    /// Bultos 0 y Reembolso = el importe. Sale en GET api/RepartoPropio/Envios como «NotaCobro» (NC{pedido}) y no en la furgoneta.</item>
    /// <item>Se asigna a un repartidor (NestoAPI#786, 04/11/26): solo él la ve en «Mis envíos» de Ariadna (GET Envios con soloMios).</item>
    /// <item>Al entregarla, el cobro (efectivo a la caja del repartidor, tarjeta por enlace o sin cobrar con autorización, #773)
    /// liquida esos efectos (<see cref="ServicioRepartoPropio"/>).</item>
    /// </list>
    /// Solo para clientes de nuestra ruta (reparto propio encendido y ruta propia de la ficha).
    /// </summary>
    public class ServicioNotasCobro
    {
        public const string MENSAJE_FALTA_SCRIPT = "No se pueden crear notas de cobro: falta lanzar el script Scripts/Issue786_NotasCobro.sql. " +
            "Avise a Informática.";
        public const string MENSAJE_FALTA_SCRIPT_REPARTIDOR = "No se pueden crear notas de cobro: falta lanzar el script " +
            "Scripts/Issue786b_NotaCobroRepartidor.sql. Avise a Informática.";
        public const string HISTORIA_PARADA = "Nota de cobro en reparto propio";
        public const string TABLA_MODIFICACIONES = "Pedidos";
        internal const int MAXIMO_EFECTOS = 50;
        internal const int LONGITUD_USUARIO_PEDIDO = 30;
        internal const int MAXIMO_COMENTARIO = 500;
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        private readonly IRepositorioNotasCobro repositorio;
        private readonly ServicioRepartidores repartidores;
        private readonly Func<bool> repartoPropioActivo;
        private readonly Func<DateTime> ahora;

        /// <param name="repartidores">NestoAPI#786: a quién se puede asignar (GET api/RepartoPropio/Repartidores).</param>
        public ServicioNotasCobro(IRepositorioNotasCobro repositorio, ServicioRepartidores repartidores, Func<bool> repartoPropioActivo = null,
            Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.repartidores = repartidores ?? throw new ArgumentNullException(nameof(repartidores));
            this.repartoPropioActivo = repartoPropioActivo ?? InterruptorRepartoPropio.EstaActivo;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<ResultadoNotaCobro> Crear(CrearNotaCobroDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            if (!repositorio.ScriptRepartidorAplicado())
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, MENSAJE_FALTA_SCRIPT_REPARTIDOR);
            }
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, "Indique el cliente al que hay que ir a cobrar.");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            if (empresa != Constantes.Empresas.EMPRESA_POR_DEFECTO)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido,
                    "Las notas de cobro son del reparto propio de la empresa 1 (los efectos pueden ser de cualquier empresa).");
            }
            if (!repartoPropioActivo())
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, "El reparto propio está apagado: no se pueden crear notas de cobro.");
            }
            if (string.IsNullOrWhiteSpace(peticion.Repartidor))
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, "Indique el repartidor que va a ir a cobrar.");
            }
            RepartidorDTO repartidor = await repartidores.Buscar(peticion.Repartidor, usuario).ConfigureAwait(false);
            if (repartidor == null)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido,
                    $"«{peticion.Repartidor.Trim()}» no es un repartidor: elija uno de la lista.");
            }
            string cliente = peticion.Cliente.Trim();
            string comentario = string.IsNullOrWhiteSpace(peticion.Comentario) ? null : peticion.Comentario.Trim();
            if (comentario != null && comentario.Length > MAXIMO_COMENTARIO)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, $"El comentario no puede pasar de {MAXIMO_COMENTARIO} caracteres.");
            }

            // Los efectos: del cliente y pendientes de cobro (con la tarjeta y el efectivo se liquidan en este orden)
            List<int> ids = (peticion.Efectos ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
            if (ids.Count > MAXIMO_EFECTOS)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, $"Como mucho {MAXIMO_EFECTOS} efectos en una nota de cobro.");
            }
            List<EfectoCobroReparto> leidos = ids.Count == 0 ? new List<EfectoCobroReparto>()
                : await repositorio.LeerEfectos(ids).ConfigureAwait(false) ?? new List<EfectoCobroReparto>();
            var efectos = new List<EfectoCobroReparto>();
            foreach (int id in ids)
            {
                EfectoCobroReparto efecto = leidos.FirstOrDefault(e => e.Id == id);
                if (efecto == null)
                {
                    return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, $"El efecto {id} no existe.");
                }
                string texto = NombreEfecto(efecto);
                if (!string.Equals(efecto.Cliente?.Trim(), cliente, StringComparison.OrdinalIgnoreCase))
                {
                    return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, $"El efecto {texto} es del cliente {efecto.Cliente?.Trim()}, no del {cliente}.");
                }
                if (efecto.ImportePendiente <= 0 || efecto.Estado?.Trim() == Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA)
                {
                    return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido,
                        $"El efecto {texto} no está pendiente de cobro: quítelo (en una nota de cobro solo van efectos pendientes, sin abonos).");
                }
                efectos.Add(efecto);
            }
            decimal pendiente = Math.Round(efectos.Sum(e => e.ImportePendiente), 2, MidpointRounding.AwayFromZero);
            if (efectos.Count == 0 && !peticion.Importe.HasValue)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, "Indique los efectos o el importe que hay que cobrar.");
            }
            decimal importe = Math.Round(peticion.Importe ?? pendiente, 2, MidpointRounding.AwayFromZero);
            if (importe <= 0)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, "El importe a cobrar tiene que ser mayor que cero.");
            }

            string contacto = !string.IsNullOrWhiteSpace(peticion.Contacto) ? peticion.Contacto.Trim() : efectos.FirstOrDefault()?.Contacto?.Trim();
            if (string.IsNullOrEmpty(contacto))
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido, "Indique el contacto (la dirección) al que hay que ir a cobrar.");
            }
            FichaNotaCobro ficha = await repositorio.LeerFicha(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.NoEncontrado, $"No existe el cliente {cliente}/{contacto}.");
            }
            if (!RutasRepartoPropio.EsRutaPropia(ficha.Ruta))
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Invalido,
                    $"La nota de cobro es para nuestra ruta (reparto propio) y el cliente {cliente}/{contacto} tiene la ruta «{ficha.Ruta?.Trim()}». " +
                    "Cóbrelo de otra forma (enlace de pago desde Nesto).");
            }
            List<int> enReparto = await repositorio.LeerNotasEnReparto(empresa, cliente, contacto).ConfigureAwait(false) ?? new List<int>();
            if (enReparto.Count > 0)
            {
                return ResultadoNotaCobro.Mal(TipoResultadoNotaEntrega.Conflicto,
                    $"El cliente {cliente}/{contacto} ya tiene la nota de cobro {CodigoNotaEntrega.DocumentoCobro(enReparto[0])} en el reparto: " +
                    "cóbrela o anule su parada antes de crear otra.");
            }

            DateTime momento = ahora();
            DateTime fecha = peticion.Fecha.HasValue && peticion.Fecha.Value.Date > momento.Date ? peticion.Fecha.Value.Date : momento.Date;
            string empresaAgencia = await repositorio.LeerEmpresaAgencia().ConfigureAwait(false);
            int numero = repositorio.ReservarNumeroPedido();
            CabPedidoVta cabecera = ConstruirCabecera(empresa, numero, cliente, contacto, ficha, importe, efectos, comentario, momento, usuario);
            var destino = new DestinoNotaEntrega
            {
                Cliente = cliente,
                Contacto = contacto,
                Nombre = ficha.Nombre,
                Direccion = ficha.Direccion,
                CodPostal = ficha.CodPostal,
                Poblacion = ficha.Poblacion,
                Provincia = ficha.Provincia,
                Telefonos = ficha.Telefonos,
                Vendedor = ficha.Vendedor,
                Texto = Observaciones(importe, comentario)
            };
            EnviosAgencia envio = ServicioParadasNotaEntrega.ConstruirEnvio(cabecera, destino, empresaAgencia, 0, usuario, fecha, importe, sinBultos: true);
            var fila = new FilaNotaEntregaCabecera
            {
                Empresa = empresa,
                Pedido = numero,
                Tipo = TiposNotaEntrega.COBRO,
                Bultos = 0,
                Usuario = usuario
            };
            var modificacion = new Modificacion
            {
                Tabla = TABLA_MODIFICACIONES,
                Anterior = $"Nota de cobro {numero}: nueva",
                Nuevo = Recortar($"Nota de cobro {numero} de {Euros(importe)} para {cliente}/{contacto}, la cobra {repartidor.Usuario}" +
                    (efectos.Count == 0 ? " (solo importe)" : $", efectos {string.Join(", ", efectos.Select(e => e.Id.ToString(CultureInfo.InvariantCulture)))}") +
                    $" (NestoAPI#786, usuario {usuario})", 4000),
                Usuario = Recortar(usuario, 50)
            };

            int envioId;
            try
            {
                envioId = await repositorio.Guardar(cabecera, fila, importe, efectos.Select(e => e.Id).ToList(), envio,
                    $"{HISTORIA_PARADA} (la cobra {repartidor.Usuario})", modificacion, repartidor.Usuario).ConfigureAwait(false);
            }
            catch
            {
                DevolverNumero(numero);
                throw;
            }

            return ResultadoNotaCobro.Bien(new NotaCobroDTO
            {
                Empresa = empresa,
                Pedido = numero,
                Cliente = cliente,
                Contacto = contacto,
                Nombre = ficha.Nombre,
                Ruta = ficha.Ruta?.Trim(),
                Importe = importe,
                Efectos = efectos.Select(AEfectoDto).ToList(),
                CodigoDocumento = CodigoNotaEntrega.DocumentoCobro(numero),
                EnvioId = envioId,
                Fecha = fecha,
                Texto = Texto(importe, efectos),
                Repartidor = repartidor.Usuario
            });
        }

        /// <summary>
        /// La cabecera (pura): cliente/contacto de la nota, NotaEntrega = 1, serie por defecto, Periodo NRM, al contado con vencimiento
        /// hoy y efectivo (sin CCC: no hay nada que domiciliar), IVA, vendedor, ruta y contacto de cobro de la ficha; en Comentarios lo
        /// que hay que cobrar. Sin líneas.
        /// </summary>
        internal static CabPedidoVta ConstruirCabecera(string empresa, int numero, string cliente, string contacto, FichaNotaCobro ficha,
            decimal importe, IReadOnlyList<EfectoCobroReparto> efectos, string comentario, DateTime hoy, string usuario)
            => new CabPedidoVta
            {
                Empresa = empresa,
                Número = numero,
                Nº_Cliente = cliente,
                Contacto = contacto,
                Fecha = hoy.Date,
                Forma_Pago = Constantes.FormasPago.EFECTIVO,
                PlazosPago = Constantes.PlazosPago.CONTADO,
                Primer_Vencimiento = hoy.Date,
                IVA = ficha.Iva,
                Vendedor = ficha.Vendedor,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Ruta = ficha.Ruta,
                Serie = Constantes.Series.SERIE_POR_DEFECTO,
                Origen = empresa,
                ContactoCobro = string.IsNullOrWhiteSpace(ficha.ContactoCobro) ? contacto : ficha.ContactoCobro,
                MantenerJunto = false,
                ServirJunto = true,
                ModoServicio = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                NotaEntrega = true,
                Comentarios = "Nota de cobro: " + Texto(importe, efectos).Replace("\n", " - ") + (comentario == null ? string.Empty : " — " + comentario),
                Usuario = Recortar(usuario, LONGITUD_USUARIO_PEDIDO)
            };

        /// <summary>
        /// Lo que ve el repartidor en la parada: «Cobro de 134,16 €» y, por efecto, «Factura NV26/001234/1 (vto. 30/10/26): 134,16 €»
        /// (lo pendiente), separados por \n. La misma regla en la lista del reparto (con lo pendiente de ese momento).
        /// </summary>
        public static string Texto(decimal importe, IEnumerable<EfectoCobroReparto> efectos)
        {
            var lineas = new List<string> { $"Cobro de {Euros(importe)}" };
            lineas.AddRange((efectos ?? Enumerable.Empty<EfectoCobroReparto>()).Select(e =>
                $"{(e.TipoApunte?.Trim() == Constantes.ExtractosCliente.TiposApunte.IMPAGADO ? "Impagado" : "Factura")} {NombreEfecto(e)} " +
                $"(vto. {e.Vencimiento.ToString("dd/MM/yy", Espanol)}): {Euros(e.ImportePendiente)}"));
            return string.Join("\n", lineas);
        }

        /// <summary>Las observaciones de la parada (máx. 80): «Cobrar 134,16 €» y el comentario.</summary>
        internal static string Observaciones(decimal importe, string comentario)
            => $"Cobrar {Euros(importe)}" + (comentario == null ? string.Empty : ". " + comentario);

        public static EfectoNotaCobroDTO AEfectoDto(EfectoCobroReparto e) => new EfectoNotaCobroDTO
        {
            Id = e.Id,
            Empresa = e.Empresa?.Trim(),
            Documento = e.Documento?.Trim(),
            Efecto = e.Efecto?.Trim(),
            Vencimiento = e.Vencimiento,
            Importe = e.Importe,
            ImportePendiente = e.ImportePendiente
        };

        /// <summary>
        /// NestoAPI#786 + #807: la empresa del cobro de una nota con efectos: la 1 si todos son de la 1; si alguno es de otra, esa (la 3
        /// se cobra siempre en efectivo, así que con uno de la 3 no hay tarjeta). Null sin efectos (manda la regla del extracto).
        /// </summary>
        public static string EmpresaCobro(IEnumerable<EfectoCobroReparto> efectos)
        {
            List<string> empresas = (efectos ?? Enumerable.Empty<EfectoCobroReparto>())
                .Select(e => e.Empresa?.Trim()).Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
            if (empresas.Count == 0)
            {
                return null;
            }
            return empresas.FirstOrDefault(e => e != Constantes.Empresas.EMPRESA_POR_DEFECTO) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
        }

        internal static string NombreEfecto(EfectoCobroReparto e)
            => e.Documento?.Trim() + (string.IsNullOrWhiteSpace(e.Efecto) ? string.Empty : "/" + e.Efecto.Trim());

        internal static string Euros(decimal importe) => importe.ToString("N2", Espanol) + " €";

        private void DevolverNumero(int numero)
        {
            try
            {
                repositorio.DevolverNumeroPedido(numero);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Nota de cobro #786] No se ha podido devolver el número de pedido {numero}: {ex.Message}", ex));
            }
        }

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>NestoAPI#786: lo que contesta <see cref="ServicioNotasCobro.Crear"/>; el controlador lo traduce a 201/400/404/409.</summary>
    public class ResultadoNotaCobro
    {
        public TipoResultadoNotaEntrega Tipo { get; private set; }
        public string Mensaje { get; private set; }
        public NotaCobroDTO Contenido { get; private set; }

        public static ResultadoNotaCobro Bien(NotaCobroDTO contenido) => new ResultadoNotaCobro { Tipo = TipoResultadoNotaEntrega.Ok, Contenido = contenido };
        public static ResultadoNotaCobro Mal(TipoResultadoNotaEntrega tipo, string mensaje) => new ResultadoNotaCobro { Tipo = tipo, Mensaje = mensaje };
    }

    /// <summary>
    /// NestoAPI#786: NotasEntregaCabecera (con su columna ImporteCobro) y NotasEntregaCobroEfectos por SQL, fuera del EDMX como el resto
    /// de las notas (#757); el pedido, la ficha, los efectos y el envío, por EF.
    /// </summary>
    public class RepositorioNotasCobro : IRepositorioNotasCobro
    {
        public const string TABLA_EFECTOS = "NotasEntregaCobroEfectos";
        public const string COLUMNA_IMPORTE = "ImporteCobro";
        /// <summary>NestoAPI#786: NotasEntregaCabecera.Repartidor (Scripts/Issue786b_NotaCobroRepartidor.sql).</summary>
        public const string COLUMNA_REPARTIDOR = "Repartidor";

        internal const string SQL_INSERTAR_CABECERA = "INSERT INTO dbo." + RepositorioNotasEntregaCabecera.TABLA +
            " (Empresa, Pedido, Tipo, Bultos, FacturaOriginal, Usuario, FechaModificacion, " + COLUMNA_IMPORTE + ", " + COLUMNA_REPARTIDOR + ") " +
            "VALUES (@Empresa, @Pedido, @Tipo, 0, NULL, @Usuario, GETDATE(), @Importe, @Repartidor)";

        internal const string SQL_INSERTAR_EFECTO = "INSERT INTO dbo." + TABLA_EFECTOS + " (Empresa, Pedido, ExtractoId, Orden) " +
            "VALUES (@Empresa, @Pedido, @Extracto, @Orden)";

        private readonly NVEntities db;

        public RepositorioNotasCobro(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado()
            => ColumnasOpcionales.ExisteTabla(db, TABLA_EFECTOS) && ColumnasOpcionales.Existen(db, RepositorioNotasEntregaCabecera.TABLA, COLUMNA_IMPORTE);

        public bool ScriptRepartidorAplicado() => ColumnasOpcionales.Existen(db, RepositorioNotasEntregaCabecera.TABLA, COLUMNA_REPARTIDOR);

        public async Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto)
        {
            List<Cliente> fichas = await db.Clientes.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                .ToListAsync().ConfigureAwait(false);
            Cliente ficha = fichas.FirstOrDefault();
            return ficha == null ? null : new FichaNotaCobro
            {
                Nombre = ficha.Nombre?.Trim(),
                Direccion = ficha.Dirección?.Trim(),
                CodPostal = ficha.CodPostal?.Trim(),
                Poblacion = ficha.Población?.Trim(),
                Provincia = ficha.Provincia?.Trim(),
                Telefonos = ficha.Teléfono?.Trim(),
                Vendedor = ficha.Vendedor?.Trim(),
                Ruta = ficha.Ruta?.Trim(),
                Iva = ficha.IVA?.Trim(),
                ContactoCobro = ficha.ContactoCobro?.Trim()
            };
        }

        public Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids) => new RepositorioCobrosReparto(db).LeerEfectos(ids);

        public async Task<List<int>> LeerNotasEnReparto(string empresa, string cliente, string contacto)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, RepositorioNotasEntregaCabecera.TABLA))
            {
                return new List<int>();
            }
            return await db.Database.SqlQuery<int>(
                "SELECT DISTINCT n.Pedido FROM dbo." + RepositorioNotasEntregaCabecera.TABLA + " n WITH (NOLOCK) " +
                "INNER JOIN dbo.EnviosAgencia e WITH (NOLOCK) ON e.Pedido = n.Pedido AND e.Agencia = @p3 AND e.Estado IN (@p4, @p5) " +
                "WHERE n.Empresa = @p0 AND n.Tipo = '" + TiposNotaEntrega.COBRO + "' AND e.Cliente = @p1 AND e.Contacto = @p2",
                empresa, cliente, contacto, Constantes.Agencias.AGENCIA_REPARTO_PROPIO,
                (int)Constantes.Agencias.ESTADO_TRAMITADO, (int)Constantes.Agencias.ESTADO_INCIDENTADO).ToListAsync().ConfigureAwait(false);
        }

        public async Task<string> LeerEmpresaAgencia()
            => (await db.AgenciasTransportes.AsNoTracking().Where(a => a.Numero == Constantes.Agencias.AGENCIA_REPARTO_PROPIO).Select(a => a.Empresa)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault()?.Trim();

        public int ReservarNumeroPedido() => db.TomarSiguienteNumeroPedido();

        public void DevolverNumeroPedido(int numero) => db.DevolverNumeroPedido(numero);

        public async Task<int> Guardar(CabPedidoVta cabecera, FilaNotaEntregaCabecera fila, decimal importe, IReadOnlyList<int> efectos,
            EnviosAgencia envio, string historia, Modificacion modificacion, string repartidor)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    _ = db.CabPedidoVtas.Add(cabecera);
                    // Comisiones: los vendedores por grupo de producto del cliente, como al crear cualquier pedido.
                    GestorComisiones.CrearVendedorPedidoGrupoProducto(db, cabecera, new PedidoVentaDTO { Usuario = cabecera.Usuario });
                    if (modificacion != null)
                    {
                        _ = db.Modificaciones.Add(modificacion);
                    }
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);

                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_CABECERA,
                        Parametro("@Empresa", SqlDbType.Char, fila.Empresa),
                        Parametro("@Pedido", SqlDbType.Int, fila.Pedido),
                        Parametro("@Tipo", SqlDbType.VarChar, fila.Tipo),
                        Parametro("@Usuario", SqlDbType.VarChar, Recortar(fila.Usuario, 50) ?? "NestoAPI"),
                        new SqlParameter("@Importe", SqlDbType.Decimal) { Precision = 12, Scale = 2, Value = importe },
                        Parametro("@Repartidor", SqlDbType.VarChar, Recortar(repartidor, 50))).ConfigureAwait(false);
                    short orden = 0;
                    foreach (int efecto in efectos ?? new List<int>())
                    {
                        orden++;
                        _ = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_EFECTO,
                            Parametro("@Empresa", SqlDbType.Char, fila.Empresa),
                            Parametro("@Pedido", SqlDbType.Int, fila.Pedido),
                            Parametro("@Extracto", SqlDbType.Int, efecto),
                            Parametro("@Orden", SqlDbType.SmallInt, orden)).ConfigureAwait(false);
                    }

                    _ = db.EnviosAgencias.Add(envio);
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    envio.CodigoBarras = CodigoBultoRepartoPropio.Albaran(envio.Numero);
                    _ = db.EnviosHistorias.Add(new EnvioHistoria
                    {
                        NumeroEnvio = envio.Numero,
                        Campo = RepositorioParadasNotaEntrega.CAMPO_HISTORIA,
                        ValorAnterior = string.Empty,
                        Observaciones = historia,
                        Usuario = envio.Usuario,
                        FechaModificacion = DateTime.Now
                    });
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return envio.Numero;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    db.Entry(cabecera).State = EntityState.Detached;
                    db.Entry(envio).State = EntityState.Detached;
                    throw;
                }
            }
        }

        private class FilaDatos
        {
            public int Pedido { get; set; }
            public decimal? Importe { get; set; }
            public string Repartidor { get; set; }
        }

        private class FilaEfecto
        {
            public int Pedido { get; set; }
            public int ExtractoId { get; set; }
            public short Orden { get; set; }
        }

        public async Task<Dictionary<int, DatosNotaCobro>> LeerDatos(string empresa, IReadOnlyCollection<int> pedidos)
        {
            var resultado = new Dictionary<int, DatosNotaCobro>();
            List<int> lista = (pedidos ?? new int[0]).Where(p => p > 0).Distinct().ToList();
            if (lista.Count == 0 || !ScriptAplicado())
            {
                return resultado;
            }
            // Enteros en el texto (sin inyección posible).
            string ids = string.Join(", ", lista.Select(p => p.ToString(CultureInfo.InvariantCulture)));
            string repartidor = ScriptRepartidorAplicado() ? $"RTRIM({COLUMNA_REPARTIDOR})" : "CAST(NULL AS varchar(50))";
            List<FilaDatos> cabeceras = await db.Database.SqlQuery<FilaDatos>(
                $"SELECT Pedido, {COLUMNA_IMPORTE} AS Importe, {repartidor} AS Repartidor FROM dbo.{RepositorioNotasEntregaCabecera.TABLA} WITH (NOLOCK) " +
                $"WHERE Empresa = @p0 AND Pedido IN ({ids}) AND Tipo = '{TiposNotaEntrega.COBRO}'", empresa).ToListAsync().ConfigureAwait(false);
            if (cabeceras.Count == 0)
            {
                return resultado;
            }
            List<FilaEfecto> efectos = await db.Database.SqlQuery<FilaEfecto>(
                $"SELECT Pedido, ExtractoId, Orden FROM dbo.{TABLA_EFECTOS} WITH (NOLOCK) WHERE Empresa = @p0 AND Pedido IN ({ids})", empresa)
                .ToListAsync().ConfigureAwait(false);
            foreach (FilaDatos cabecera in cabeceras)
            {
                resultado[cabecera.Pedido] = new DatosNotaCobro
                {
                    Pedido = cabecera.Pedido,
                    Importe = cabecera.Importe ?? 0m,
                    Repartidor = ServicioRepartoPropio.NombreRepartidor(cabecera.Repartidor),
                    Efectos = efectos.Where(e => e.Pedido == cabecera.Pedido).OrderBy(e => e.Orden).Select(e => e.ExtractoId).ToList()
                };
            }
            return resultado;
        }

        private static SqlParameter Parametro(string nombre, SqlDbType tipo, object valor)
            => new SqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value };

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
