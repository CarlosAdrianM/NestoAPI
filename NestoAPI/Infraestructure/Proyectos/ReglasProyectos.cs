using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Proyectos
{
    /*
        NestoAPI#901 (Carlos 25/11/26, primer corte): PROYECTOS. Cada convocatoria de un curso del SEPE (IMPE0210-2026-1), feria,
        evento o campaña es un proyecto, y se le imputan DESPUÉS de contabilizar apuntes de la contabilidad (cuentas 6 y 7) por IMPORTE,
        para sacar su cuenta de explotación. La contabilidad no se toca: las imputaciones van aparte (dbo.ProyectosImputaciones) con una
        FOTO del apunte.

        CONVENCIÓN DE SIGNOS (una sola, aquí):
        - El importe del apunte es |Debe − Haber| (siempre ≥ 0). El LADO es Debe si Debe > Haber, Haber si Haber > Debe.
        - La imputación guarda SIEMPRE un importe POSITIVO (la parte del apunte que va al proyecto), nunca más que el apunte.
        - La NATURALEZA sale de la cuenta: 6xx = Gasto, 7xx = Ingreso (otras cuentas no se imputan en este corte).
        - El lado NATURAL de un gasto es el Debe y el de un ingreso el Haber. En su lado natural la imputación SUMA; al lado contrario
          (un abono de compra en el Haber de la 6xx, una rectificativa de venta en el Debe de la 7xx) RESTA. Ese es el «efecto» que
          entra en la cuenta de explotación: Resultado = Ingresos − Gastos, con los abonos ya restados.

        SEGUNDO CORTE (preparado, NO implementado): cuentas obligatorias con control «sin imputar», reglas que proponen (proveedor + cuenta
        → proyecto abierto), horas de personal propio (línea de coste interno sin apunte; entraría en la cuenta de explotación como un
        gasto más con Naturaleza Gasto y sin NumeroOrdenApunte) y propuesta automática de las facturas de las inscripciones del evento.
    */

    /// <summary>NestoAPI#901: gasto o ingreso, según la cuenta.</summary>
    public enum NaturalezaApunte
    {
        NoImputable = 0,
        Gasto = 1,
        Ingreso = 2
    }

    /// <summary>NestoAPI#901: un proyecto tal como está en dbo.Proyectos.</summary>
    public class FilaProyecto
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Estado { get; set; }
        public string Producto { get; set; }
        public int? EventoId { get; set; }
        public decimal? PresupuestoIngresos { get; set; }
        public decimal? PresupuestoGastos { get; set; }
        public string Comentarios { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaAlta { get; set; }
        public string UsuarioModificacion { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public FilaProyecto Copia() => (FilaProyecto)MemberwiseClone();

        public bool EstaCerrado => string.Equals(Estado?.Trim(), ReglasProyectos.ESTADO_CERRADO, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>NestoAPI#901: un apunte de Contabilidad (lo que se lee para imputarlo y lo que se copia en la foto).</summary>
    public class FilaApunteContable
    {
        public string Empresa { get; set; }
        public int NumeroOrden { get; set; }
        public string Cuenta { get; set; }
        public string NombreCuenta { get; set; }
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        public string Documento { get; set; }
        public int? Asiento { get; set; }
        public string Diario { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
        /// <summary>Proveedor o cliente del asiento (ExtractoProveedor / ExtractoCliente con el mismo asiento), si lo hay.</summary>
        public string Tercero { get; set; }
        /// <summary>«Proveedor», «Cliente» o null.</summary>
        public string TipoTercero { get; set; }
        public string NombreTercero { get; set; }
        /// <summary>Solo en la lista de apuntes por imputar: lo imputado ya a proyectos (suma de importes positivos).</summary>
        public decimal Imputado { get; set; }
    }

    /// <summary>NestoAPI#901: una fila de dbo.ProyectosImputaciones (con la foto del apunte).</summary>
    public class FilaImputacion
    {
        public int Id { get; set; }
        public int ProyectoId { get; set; }
        public string Empresa { get; set; }
        public int NumeroOrdenApunte { get; set; }
        /// <summary>Siempre positivo: la parte del apunte que va al proyecto.</summary>
        public decimal Importe { get; set; }
        public string Cuenta { get; set; }
        public string NombreCuenta { get; set; }
        public DateTime FechaApunte { get; set; }
        public string Concepto { get; set; }
        public string Documento { get; set; }
        public int? Asiento { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
        public string Tercero { get; set; }
        public string TipoTercero { get; set; }
        public string NombreTercero { get; set; }
        public string Comentario { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>Solo lectura (join): datos del proyecto.</summary>
        public string CodigoProyecto { get; set; }
        public string NombreProyecto { get; set; }
        public string EstadoProyecto { get; set; }
    }

    /// <summary>NestoAPI#901: una imputación pedida en el PUT del reparto de un apunte.</summary>
    public class ImputacionPedida
    {
        public int ProyectoId { get; set; }
        public decimal Importe { get; set; }
        public string Comentario { get; set; }
    }

    /// <summary>NestoAPI#901: lo que el repositorio lee DENTRO de la transacción (con el bloqueo del apunte) para decidir.</summary>
    public class ContextoImputacionApunte
    {
        public FilaApunteContable Apunte { get; set; }
        public List<FilaImputacion> Actuales { get; set; } = new List<FilaImputacion>();
        /// <summary>Los proyectos de las imputaciones actuales y de las pedidas (los que no existen, no están).</summary>
        public Dictionary<int, FilaProyecto> Proyectos { get; set; } = new Dictionary<int, FilaProyecto>();
    }

    /// <summary>NestoAPI#901: el desenlace de una regla: OK, o el error con su tipo (400/404/409) y un código para los clientes.</summary>
    public enum TipoErrorProyecto
    {
        Ninguno = 0,
        Invalido = 400,
        Prohibido = 403,
        NoExiste = 404,
        Conflicto = 409,
        SinTabla = 503
    }

    public class ErrorProyecto
    {
        public TipoErrorProyecto Tipo { get; set; }
        public string Codigo { get; set; }
        public string Mensaje { get; set; }

        public static ErrorProyecto Invalido(string mensaje, string codigo = "DATOS_NO_VALIDOS") =>
            new ErrorProyecto { Tipo = TipoErrorProyecto.Invalido, Codigo = codigo, Mensaje = mensaje };

        public static ErrorProyecto Conflicto(string codigo, string mensaje) =>
            new ErrorProyecto { Tipo = TipoErrorProyecto.Conflicto, Codigo = codigo, Mensaje = mensaje };

        public static ErrorProyecto NoExiste(string mensaje) =>
            new ErrorProyecto { Tipo = TipoErrorProyecto.NoExiste, Codigo = "NO_EXISTE", Mensaje = mensaje };

        public static ErrorProyecto Prohibido(string mensaje) =>
            new ErrorProyecto { Tipo = TipoErrorProyecto.Prohibido, Codigo = "SIN_PERMISO", Mensaje = mensaje };
    }

    /// <summary>NestoAPI#901: lo que decide LA REGLA del reparto de un apunte (y el repositorio escribe tal cual).</summary>
    public class DecisionImputaciones
    {
        public ErrorProyecto Error { get; set; }
        /// <summary>No hay nada que cambiar: ni se escribe ni se deja rastro.</summary>
        public bool SinCambios { get; set; }
        /// <summary>Ids de dbo.ProyectosImputaciones que se borran.</summary>
        public List<int> IdsABorrar { get; set; } = new List<int>();
        /// <summary>Filas que se insertan (con la foto del apunte de ahora).</summary>
        public List<FilaImputacion> Insertar { get; set; } = new List<FilaImputacion>();
        /// <summary>Cómo queda el reparto (lo que se queda de proyectos cerrados + lo insertado), para el rastro y la respuesta.</summary>
        public List<FilaImputacion> Resultado { get; set; } = new List<FilaImputacion>();
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#901: lo que decide la regla al tocar un proyecto (modificar, cerrar, reabrir o borrar).</summary>
    public class DecisionCambioProyecto
    {
        public ErrorProyecto Error { get; set; }
        public bool SinCambios { get; set; }
        public bool Borrar { get; set; }
        /// <summary>Cómo queda (si no se borra).</summary>
        public FilaProyecto Nuevo { get; set; }
    }

    /// <summary>NestoAPI#901: lo que se pide al dar de alta o modificar un proyecto (sin estado: se abre al crearlo y se cambia con Cerrar/Reabrir).</summary>
    public class DatosProyecto
    {
        public string Empresa { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Producto { get; set; }
        public int? EventoId { get; set; }
        public decimal? PresupuestoIngresos { get; set; }
        public decimal? PresupuestoGastos { get; set; }
        public string Comentarios { get; set; }
    }

    /// <summary>NestoAPI#901: LAS REGLAS de los proyectos (puras).</summary>
    public static class ReglasProyectos
    {
        public const string ESTADO_ABIERTO = "Abierto";
        public const string ESTADO_CERRADO = "Cerrado";
        public static readonly string[] TIPOS = { "CursoSEPE", "Evento", "Feria", "Campana", "Otro" };
        public const int LONGITUD_CODIGO = 20;
        public const int LONGITUD_NOMBRE = 100;
        public const int LONGITUD_PRODUCTO = 15;
        public const int LONGITUD_COMENTARIOS = 1000;
        public const int LONGITUD_COMENTARIO_IMPUTACION = 200;
        /// <summary>La suma de lo imputado puede pasar del apunte como mucho 1 céntimo (redondeos de un reparto a partes iguales).</summary>
        public const decimal TOLERANCIA = 0.01M;

        /// <summary>Código en mayúsculas, sin espacios alrededor; null si viene vacío.</summary>
        public static string NormalizarCodigo(string codigo) =>
            string.IsNullOrWhiteSpace(codigo) ? null : codigo.Trim().ToUpperInvariant();

        /// <summary>
        /// Código válido: 1..20 caracteres, letras A-Z sin tildes, cifras y los separadores «-», «_», «.» y «/», empezando por letra o
        /// cifra (IMPE0210-2026-1, FERIA-2026, BF2026). Devuelve el motivo (de usted) o null si vale.
        /// </summary>
        public static string MotivoCodigoNoValido(string codigo)
        {
            string limpio = NormalizarCodigo(codigo);
            if (limpio == null)
            {
                return "Falta el código del proyecto (por ejemplo, IMPE0210-2026-1).";
            }
            if (limpio.Length > LONGITUD_CODIGO)
            {
                return $"El código del proyecto puede tener como mucho {LONGITUD_CODIGO} caracteres («{limpio}» tiene {limpio.Length}).";
            }
            if (!EsLetraOCifra(limpio[0]))
            {
                return $"El código del proyecto tiene que empezar por una letra o una cifra («{limpio}»).";
            }
            foreach (char c in limpio)
            {
                if (!EsLetraOCifra(c) && c != '-' && c != '_' && c != '.' && c != '/')
                {
                    return $"El código del proyecto solo admite letras sin tildes, cifras y los signos - _ . / («{limpio}» lleva «{c}»).";
                }
            }
            return null;
        }

        private static bool EsLetraOCifra(char c) => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        /// <summary>El tipo tal como se guarda (sin distinguir mayúsculas ni tildes: «curso sepe», «Campaña»), o null si no es ninguno.</summary>
        public static string NormalizarTipo(string tipo)
        {
            if (string.IsNullOrWhiteSpace(tipo))
            {
                return null;
            }
            string sin = SinTildesNiEspacios(tipo);
            return TIPOS.FirstOrDefault(t => string.Equals(t, sin, StringComparison.OrdinalIgnoreCase));
        }

        private static string SinTildesNiEspacios(string texto)
        {
            string descompuesto = texto.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Valida y deja limpio lo que se pide al crear o modificar. Devuelve los motivos (vacío = vale).</summary>
        public static List<string> Validar(DatosProyecto datos, out FilaProyecto limpio)
        {
            var faltas = new List<string>();
            limpio = new FilaProyecto();
            if (datos == null)
            {
                faltas.Add("Faltan los datos del proyecto.");
                return faltas;
            }
            limpio.Empresa = string.IsNullOrWhiteSpace(datos.Empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : datos.Empresa.Trim();
            string motivoCodigo = MotivoCodigoNoValido(datos.Codigo);
            if (motivoCodigo != null)
            {
                faltas.Add(motivoCodigo);
            }
            limpio.Codigo = NormalizarCodigo(datos.Codigo);

            string nombre = datos.Nombre?.Trim();
            if (string.IsNullOrEmpty(nombre))
            {
                faltas.Add("Falta el nombre del proyecto.");
            }
            else if (nombre.Length > LONGITUD_NOMBRE)
            {
                faltas.Add($"El nombre del proyecto puede tener como mucho {LONGITUD_NOMBRE} caracteres.");
            }
            limpio.Nombre = nombre;

            limpio.Tipo = NormalizarTipo(datos.Tipo);
            if (limpio.Tipo == null)
            {
                faltas.Add(string.IsNullOrWhiteSpace(datos.Tipo)
                    ? $"Falta el tipo del proyecto: {string.Join(", ", TIPOS)}."
                    : $"«{datos.Tipo.Trim()}» no es un tipo de proyecto: tiene que ser {string.Join(", ", TIPOS)}.");
            }

            if (!datos.FechaDesde.HasValue)
            {
                faltas.Add("Falta la fecha de inicio del proyecto.");
            }
            else
            {
                limpio.FechaDesde = datos.FechaDesde.Value.Date;
            }
            limpio.FechaHasta = datos.FechaHasta?.Date;
            if (datos.FechaDesde.HasValue && limpio.FechaHasta.HasValue && limpio.FechaHasta.Value < limpio.FechaDesde)
            {
                faltas.Add("La fecha de fin del proyecto no puede ser anterior a la de inicio.");
            }

            string producto = datos.Producto?.Trim();
            if (!string.IsNullOrEmpty(producto) && producto.Length > LONGITUD_PRODUCTO)
            {
                faltas.Add($"La referencia del producto puede tener como mucho {LONGITUD_PRODUCTO} caracteres.");
            }
            limpio.Producto = string.IsNullOrEmpty(producto) ? null : producto.ToUpperInvariant();

            if (datos.EventoId.HasValue && datos.EventoId.Value <= 0)
            {
                faltas.Add("El evento tiene que ser un número mayor que cero.");
            }
            limpio.EventoId = datos.EventoId;

            if (datos.PresupuestoIngresos < 0 || datos.PresupuestoGastos < 0)
            {
                faltas.Add("El presupuesto no puede ser negativo.");
            }
            limpio.PresupuestoIngresos = datos.PresupuestoIngresos.HasValue ? RoundingHelper.DosDecimalesRound(datos.PresupuestoIngresos.Value) : (decimal?)null;
            limpio.PresupuestoGastos = datos.PresupuestoGastos.HasValue ? RoundingHelper.DosDecimalesRound(datos.PresupuestoGastos.Value) : (decimal?)null;

            string comentarios = datos.Comentarios?.Trim();
            if (!string.IsNullOrEmpty(comentarios) && comentarios.Length > LONGITUD_COMENTARIOS)
            {
                faltas.Add($"Los comentarios pueden tener como mucho {LONGITUD_COMENTARIOS} caracteres.");
            }
            limpio.Comentarios = string.IsNullOrEmpty(comentarios) ? null : comentarios;
            limpio.Estado = ESTADO_ABIERTO;
            return faltas;
        }

        // ---------------------------------------------------------------- Ciclo de vida

        /// <summary>Modificar: un proyecto cerrado no admite cambios. La empresa y el estado no se cambian por aquí.</summary>
        public static DecisionCambioProyecto DecidirModificacion(FilaProyecto actual, FilaProyecto pedido)
        {
            if (actual == null)
            {
                return new DecisionCambioProyecto { Error = ErrorProyecto.NoExiste("No existe ese proyecto.") };
            }
            if (actual.EstaCerrado)
            {
                return new DecisionCambioProyecto { Error = ErrorProyecto.Conflicto("PROYECTO_CERRADO",
                    $"El proyecto {actual.Codigo} está cerrado: no admite cambios. Si hay que cambiarlo, pida a Dirección que lo reabra.") };
            }
            FilaProyecto nuevo = Copia(actual);
            nuevo.Codigo = pedido.Codigo;
            nuevo.Nombre = pedido.Nombre;
            nuevo.Tipo = pedido.Tipo;
            nuevo.FechaDesde = pedido.FechaDesde;
            nuevo.FechaHasta = pedido.FechaHasta;
            nuevo.Producto = pedido.Producto;
            nuevo.EventoId = pedido.EventoId;
            nuevo.PresupuestoIngresos = pedido.PresupuestoIngresos;
            nuevo.PresupuestoGastos = pedido.PresupuestoGastos;
            nuevo.Comentarios = pedido.Comentarios;
            return new DecisionCambioProyecto { Nuevo = nuevo, SinCambios = MismosDatos(actual, nuevo) };
        }

        /// <summary>Cerrar: idempotente (ya cerrado = sin cambios).</summary>
        public static DecisionCambioProyecto DecidirCierre(FilaProyecto actual)
        {
            if (actual == null)
            {
                return new DecisionCambioProyecto { Error = ErrorProyecto.NoExiste("No existe ese proyecto.") };
            }
            FilaProyecto nuevo = Copia(actual);
            nuevo.Estado = ESTADO_CERRADO;
            return new DecisionCambioProyecto { Nuevo = nuevo, SinCambios = actual.EstaCerrado };
        }

        /// <summary>Reabrir: solo Dirección (lo comprueba quien llama); idempotente.</summary>
        public static DecisionCambioProyecto DecidirReapertura(FilaProyecto actual)
        {
            if (actual == null)
            {
                return new DecisionCambioProyecto { Error = ErrorProyecto.NoExiste("No existe ese proyecto.") };
            }
            FilaProyecto nuevo = Copia(actual);
            nuevo.Estado = ESTADO_ABIERTO;
            return new DecisionCambioProyecto { Nuevo = nuevo, SinCambios = !actual.EstaCerrado };
        }

        /// <summary>Borrar: solo un proyecto abierto y sin ninguna imputación.</summary>
        public static DecisionCambioProyecto DecidirBorrado(FilaProyecto actual, int numeroImputaciones)
        {
            if (actual == null)
            {
                return new DecisionCambioProyecto { Error = ErrorProyecto.NoExiste("No existe ese proyecto.") };
            }
            if (actual.EstaCerrado)
            {
                return new DecisionCambioProyecto { Error = ErrorProyecto.Conflicto("PROYECTO_CERRADO",
                    $"El proyecto {actual.Codigo} está cerrado: no se puede borrar.") };
            }
            if (numeroImputaciones > 0)
            {
                return new DecisionCambioProyecto { Error = ErrorProyecto.Conflicto("PROYECTO_CON_IMPUTACIONES",
                    $"El proyecto {actual.Codigo} tiene {numeroImputaciones} {(numeroImputaciones == 1 ? "imputación" : "imputaciones")}: quítelas antes de borrarlo, o ciérrelo.") };
            }
            return new DecisionCambioProyecto { Borrar = true };
        }

        internal static FilaProyecto Copia(FilaProyecto p) => p.Copia();

        private static bool MismosDatos(FilaProyecto a, FilaProyecto b) =>
            a.Codigo == b.Codigo && a.Nombre == b.Nombre && a.Tipo == b.Tipo && a.FechaDesde == b.FechaDesde && a.FechaHasta == b.FechaHasta
            && a.Producto == b.Producto && a.EventoId == b.EventoId && a.PresupuestoIngresos == b.PresupuestoIngresos
            && a.PresupuestoGastos == b.PresupuestoGastos && a.Comentarios == b.Comentarios;

        // ---------------------------------------------------------------- Signos

        /// <summary>6xx = gasto, 7xx = ingreso; lo demás no se imputa en este corte.</summary>
        public static NaturalezaApunte Naturaleza(string cuenta)
        {
            string c = cuenta?.Trim();
            if (string.IsNullOrEmpty(c))
            {
                return NaturalezaApunte.NoImputable;
            }
            return c[0] == '6' ? NaturalezaApunte.Gasto : c[0] == '7' ? NaturalezaApunte.Ingreso : NaturalezaApunte.NoImputable;
        }

        /// <summary>El importe del apunte: |Debe − Haber|.</summary>
        public static decimal ImporteApunte(decimal debe, decimal haber) => Math.Abs(debe - haber);

        /// <summary>El apunte está en el lado contrario al natural de su cuenta (abono de compra en la 6xx, rectificativa de venta en la 7xx).</summary>
        public static bool EsAbono(string cuenta, decimal debe, decimal haber)
        {
            NaturalezaApunte n = Naturaleza(cuenta);
            decimal neto = debe - haber;
            return (n == NaturalezaApunte.Gasto && neto < 0) || (n == NaturalezaApunte.Ingreso && neto > 0);
        }

        /// <summary>+1 en su lado natural, −1 si es un abono (resta en la cuenta de explotación).</summary>
        public static int Signo(string cuenta, decimal debe, decimal haber) => EsAbono(cuenta, debe, haber) ? -1 : 1;

        /// <summary>Lo que la imputación suma (o resta) a los ingresos o a los gastos del proyecto.</summary>
        public static decimal Efecto(FilaImputacion imputacion) =>
            imputacion.Importe * Signo(imputacion.Cuenta, imputacion.Debe, imputacion.Haber);

        /// <summary>El porcentaje del apunte que se lleva la imputación, con 2 decimales (0 si el apunte es 0).</summary>
        public static decimal Porcentaje(decimal importeImputado, decimal importeApunte) =>
            importeApunte == 0 ? 0 : RoundingHelper.DosDecimalesRound(importeImputado / importeApunte * 100M);

        /// <summary>Resultado = ingresos − gastos.</summary>
        public static decimal Resultado(decimal ingresos, decimal gastos) => ingresos - gastos;

        /// <summary>Margen = resultado / ingresos en %, con 2 decimales; null sin ingresos.</summary>
        public static decimal? Margen(decimal ingresos, decimal gastos) =>
            ingresos == 0 ? (decimal?)null : RoundingHelper.DosDecimalesRound(Resultado(ingresos, gastos) / ingresos * 100M);

        // ---------------------------------------------------------------- Reparto de un apunte

        /// <summary>
        /// LA REGLA del reparto de un apunte entre proyectos. El PUT SUSTITUYE el reparto (todo o nada; lista vacía = quitar):
        /// el apunte tiene que existir y, si quien llama dice qué importe vio, seguir teniéndolo (si no, 409); solo cuentas 6 y 7;
        /// cada proyecto tiene que existir y ser de la misma empresa; importes &gt; 0; un proyecto una vez; un proyecto CERRADO no
        /// admite imputaciones nuevas ni cambios (lo suyo tiene que venir igual: mismo importe y comentario); y la suma no puede
        /// pasar del importe del apunte (tolerancia de 1 céntimo).
        /// </summary>
        public static DecisionImputaciones DecidirReparto(ContextoImputacionApunte contexto, int numeroOrden, decimal? importeApunteVisto,
            IList<ImputacionPedida> pedidas, string usuario, DateTime ahora)
        {
            var decision = new DecisionImputaciones();
            FilaApunteContable apunte = contexto?.Apunte;
            if (apunte == null && (pedidas == null || pedidas.Count == 0) && contexto?.Actuales?.Count > 0)
            {
                // El apunte ya no está en la contabilidad (se borró o se volvió a contabilizar): se pueden quitar sus imputaciones
                // huérfanas, salvo las de un proyecto cerrado.
                foreach (FilaImputacion huerfana in contexto.Actuales)
                {
                    if (contexto.Proyectos.TryGetValue(huerfana.ProyectoId, out FilaProyecto cerrado) && cerrado != null && cerrado.EstaCerrado)
                    {
                        decision.Error = ErrorProyecto.Conflicto("PROYECTO_CERRADO",
                            $"El proyecto {cerrado.Codigo} está cerrado: su imputación de {Euros(huerfana.Importe)} no se puede quitar.");
                        return decision;
                    }
                    decision.IdsABorrar.Add(huerfana.Id);
                }
                return decision;
            }
            if (apunte == null)
            {
                decision.Error = ErrorProyecto.Conflicto("APUNTE_NO_EXISTE",
                    $"El apunte {numeroOrden} ya no existe en la contabilidad: vuelva a buscar los apuntes por imputar.");
                return decision;
            }
            decimal importeApunte = ImporteApunte(apunte.Debe, apunte.Haber);
            if (importeApunteVisto.HasValue && Math.Abs(importeApunteVisto.Value - importeApunte) > 0.005M)
            {
                decision.Error = ErrorProyecto.Conflicto("APUNTE_CAMBIADO",
                    $"El apunte {numeroOrden} ha cambiado desde que lo abrió: ahora es de {Euros(importeApunte)} y usted vio {Euros(importeApunteVisto.Value)}. Vuelva a cargarlo.");
                return decision;
            }
            List<ImputacionPedida> lista = (pedidas ?? new List<ImputacionPedida>()).Where(p => p != null).ToList();
            List<FilaImputacion> actuales = contexto.Actuales ?? new List<FilaImputacion>();
            if (lista.Count > 0 && Naturaleza(apunte.Cuenta) == NaturalezaApunte.NoImputable)
            {
                decision.Error = ErrorProyecto.Invalido($"La cuenta {apunte.Cuenta?.Trim()} no es de gastos (6) ni de ingresos (7): no se imputa a proyectos.");
                return decision;
            }
            if (lista.Count > 0 && importeApunte == 0)
            {
                decision.Error = ErrorProyecto.Invalido($"El apunte {numeroOrden} es de 0 €: no hay nada que imputar.");
                return decision;
            }

            var faltas = new List<string>();
            foreach (IGrouping<int, ImputacionPedida> repetido in lista.GroupBy(p => p.ProyectoId).Where(g => g.Count() > 1))
            {
                faltas.Add($"El proyecto {NombreProyecto(contexto, repetido.Key)} sale {repetido.Count()} veces: ponga un solo importe por proyecto.");
            }
            foreach (ImputacionPedida p in lista)
            {
                if (!contexto.Proyectos.TryGetValue(p.ProyectoId, out FilaProyecto proyecto) || proyecto == null)
                {
                    faltas.Add($"No existe el proyecto {p.ProyectoId}.");
                    continue;
                }
                if (!string.Equals(proyecto.Empresa?.Trim(), apunte.Empresa?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    faltas.Add($"El proyecto {proyecto.Codigo} es de la empresa {proyecto.Empresa?.Trim()} y el apunte de la {apunte.Empresa?.Trim()}.");
                }
                if (RoundingHelper.DosDecimalesRound(p.Importe) <= 0)
                {
                    faltas.Add($"El importe imputado al proyecto {proyecto.Codigo} tiene que ser mayor que cero (sin signo: el abono se deduce del apunte).");
                }
                if (p.Comentario != null && p.Comentario.Trim().Length > LONGITUD_COMENTARIO_IMPUTACION)
                {
                    faltas.Add($"El comentario de la imputación al proyecto {proyecto.Codigo} puede tener como mucho {LONGITUD_COMENTARIO_IMPUTACION} caracteres.");
                }
            }
            if (faltas.Any())
            {
                decision.Error = ErrorProyecto.Invalido(string.Join(" ", faltas.Distinct()));
                return decision;
            }

            // Proyectos cerrados: lo suyo tiene que quedar exactamente igual.
            var cerrados = new HashSet<int>();
            foreach (FilaImputacion actual in actuales)
            {
                if (contexto.Proyectos.TryGetValue(actual.ProyectoId, out FilaProyecto proyecto) && proyecto != null && proyecto.EstaCerrado)
                {
                    ImputacionPedida misma = lista.FirstOrDefault(p => p.ProyectoId == actual.ProyectoId);
                    if (misma == null || RoundingHelper.DosDecimalesRound(misma.Importe) != actual.Importe
                        || (Limpio(misma.Comentario) ?? string.Empty) != (Limpio(actual.Comentario) ?? string.Empty))
                    {
                        decision.Error = ErrorProyecto.Conflicto("PROYECTO_CERRADO",
                            $"El proyecto {proyecto.Codigo} está cerrado: su imputación de {Euros(actual.Importe)} no se puede quitar ni cambiar.");
                        return decision;
                    }
                    cerrados.Add(actual.ProyectoId);
                }
            }
            foreach (ImputacionPedida p in lista.Where(p => !cerrados.Contains(p.ProyectoId)))
            {
                if (contexto.Proyectos[p.ProyectoId].EstaCerrado)
                {
                    decision.Error = ErrorProyecto.Conflicto("PROYECTO_CERRADO",
                        $"El proyecto {contexto.Proyectos[p.ProyectoId].Codigo} está cerrado: no admite imputaciones nuevas.");
                    return decision;
                }
            }

            decimal suma = lista.Sum(p => RoundingHelper.DosDecimalesRound(p.Importe));
            if (suma > importeApunte + TOLERANCIA)
            {
                decision.Error = ErrorProyecto.Invalido(
                    $"Lo imputado ({Euros(suma)}) pasa del importe del apunte {numeroOrden} ({Euros(importeApunte)}).", "IMPUTADO_PASA_DEL_APUNTE");
                return decision;
            }

            // Lo que hay que escribir.
            foreach (FilaImputacion actual in actuales)
            {
                if (cerrados.Contains(actual.ProyectoId))
                {
                    decision.Resultado.Add(actual);
                    continue;
                }
                decision.IdsABorrar.Add(actual.Id);
            }
            foreach (ImputacionPedida p in lista.Where(p => !cerrados.Contains(p.ProyectoId)))
            {
                FilaProyecto proyecto = contexto.Proyectos[p.ProyectoId];
                decimal importe = RoundingHelper.DosDecimalesRound(p.Importe);
                string comentario = Limpio(p.Comentario);
                FilaImputacion igual = actuales.FirstOrDefault(a => a.ProyectoId == p.ProyectoId && a.Importe == importe
                    && (Limpio(a.Comentario) ?? string.Empty) == (comentario ?? string.Empty));
                FilaImputacion fila = Foto(apunte);
                fila.ProyectoId = p.ProyectoId;
                fila.Importe = importe;
                fila.Comentario = comentario;
                fila.Usuario = igual?.Usuario ?? usuario;
                fila.Fecha = igual?.Fecha ?? ahora;
                fila.CodigoProyecto = proyecto.Codigo;
                fila.NombreProyecto = proyecto.Nombre;
                fila.EstadoProyecto = proyecto.Estado;
                decision.Insertar.Add(fila);
                decision.Resultado.Add(fila);

                if (apunte.Fecha.Date < proyecto.FechaDesde.Date || (proyecto.FechaHasta.HasValue && apunte.Fecha.Date > proyecto.FechaHasta.Value.Date))
                {
                    decision.Avisos.Add($"El apunte es del {apunte.Fecha:dd/MM/yyyy}, fuera de las fechas del proyecto {proyecto.Codigo} " +
                        $"({proyecto.FechaDesde:dd/MM/yyyy}{(proyecto.FechaHasta.HasValue ? " - " + proyecto.FechaHasta.Value.ToString("dd/MM/yyyy") : " en adelante")}). Se imputa igual.");
                }
            }

            bool foto = actuales.All(a => a.Debe == apunte.Debe && a.Haber == apunte.Haber && a.Cuenta?.Trim() == apunte.Cuenta?.Trim());
            decision.SinCambios = foto && decision.Insertar.Count == decision.IdsABorrar.Count
                && decision.Insertar.All(n => actuales.Any(a => a.ProyectoId == n.ProyectoId && a.Importe == n.Importe
                    && (Limpio(a.Comentario) ?? string.Empty) == (n.Comentario ?? string.Empty)));
            if (decision.SinCambios)
            {
                decision.IdsABorrar.Clear();
                decision.Insertar.Clear();
                decision.Resultado = actuales.ToList();
            }
            return decision;
        }

        /// <summary>La foto del apunte que se guarda con cada imputación.</summary>
        public static FilaImputacion Foto(FilaApunteContable apunte) => new FilaImputacion
        {
            Empresa = apunte.Empresa?.Trim(),
            NumeroOrdenApunte = apunte.NumeroOrden,
            Cuenta = apunte.Cuenta?.Trim(),
            NombreCuenta = apunte.NombreCuenta?.Trim(),
            FechaApunte = apunte.Fecha,
            Concepto = apunte.Concepto?.Trim(),
            Documento = apunte.Documento?.Trim(),
            Asiento = apunte.Asiento,
            Debe = apunte.Debe,
            Haber = apunte.Haber,
            Tercero = apunte.Tercero?.Trim(),
            TipoTercero = apunte.TipoTercero,
            NombreTercero = apunte.NombreTercero?.Trim()
        };

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private static string NombreProyecto(ContextoImputacionApunte contexto, int id) =>
            contexto.Proyectos.TryGetValue(id, out FilaProyecto p) && p != null ? p.Codigo : id.ToString(CultureInfo.InvariantCulture);

        public static string Euros(decimal importe) => importe.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
    }
}
