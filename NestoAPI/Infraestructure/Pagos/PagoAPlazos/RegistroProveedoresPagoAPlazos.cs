using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos.PagoAPlazos
{
    /// <summary>NestoAPI#864: los proveedores de pago a plazos que hay, por su código.</summary>
    public interface IRegistroProveedoresPagoAPlazos
    {
        /// <summary>El proveedor con ese código (sin mirar mayúsculas), o null si no está dado de alta.</summary>
        IProveedorPagoAPlazos PorCodigo(string codigo);

        IReadOnlyList<string> Codigos { get; }
    }

    /// <summary>
    /// NestoAPI#864: el registro de proveedores de pago a plazos. Cuál está ACTIVO es un dato (parámetro
    /// <see cref="ConfiguracionPagoAPlazos.CLAVE_PROVEEDOR"/> de «(defecto)»), no código: aquí solo se dan de alta.
    /// Una solicitud ya creada sigue con SU proveedor (va en la tabla) aunque después se cambie el activo.
    /// <para><b>Añadir una financiera</b>: una clase que implemente <see cref="IProveedorPagoAPlazos"/> y una línea en
    /// <see cref="PorDefecto"/>. Ni el núcleo, ni los endpoints, ni los clientes cambian.</para>
    /// </summary>
    public class RegistroProveedoresPagoAPlazos : IRegistroProveedoresPagoAPlazos
    {
        private readonly Dictionary<string, IProveedorPagoAPlazos> proveedores;

        public RegistroProveedoresPagoAPlazos(params IProveedorPagoAPlazos[] proveedores)
        {
            this.proveedores = new Dictionary<string, IProveedorPagoAPlazos>(StringComparer.OrdinalIgnoreCase);
            foreach (IProveedorPagoAPlazos proveedor in proveedores ?? new IProveedorPagoAPlazos[0])
            {
                if (proveedor == null || string.IsNullOrWhiteSpace(proveedor.Codigo))
                {
                    continue;
                }
                if (this.proveedores.ContainsKey(proveedor.Codigo.Trim()))
                {
                    throw new ArgumentException($"El proveedor de pago a plazos «{proveedor.Codigo.Trim()}» está dado de alta dos veces");
                }
                this.proveedores[proveedor.Codigo.Trim()] = proveedor;
            }
        }

        /// <summary>Los proveedores de verdad. Los que haya se crean sin tocar la red ni la base de datos.</summary>
        public static IRegistroProveedoresPagoAPlazos PorDefecto()
        {
            return new RegistroProveedoresPagoAPlazos(
                Aplazame.ProveedorPagoAPlazosAplazame.CrearPorDefecto());
        }

        public IProveedorPagoAPlazos PorCodigo(string codigo)
        {
            return !string.IsNullOrWhiteSpace(codigo) && proveedores.TryGetValue(codigo.Trim(), out IProveedorPagoAPlazos proveedor)
                ? proveedor
                : null;
        }

        public IReadOnlyList<string> Codigos => proveedores.Keys.ToList();
    }

    /// <summary>En qué modo está el pago a plazos de los pedidos pasados por un empleado o un vendedor.</summary>
    public enum ModoPagoAPlazos
    {
        /// <summary>No se ofrece. Es lo que vale sin fila, con un valor que no se reconoce o si el parámetro no se puede leer.</summary>
        Apagado,
        /// <summary>Solo para los clientes de prueba y contra el entorno de pruebas del proveedor (sin dinero).</summary>
        Pruebas,
        /// <summary>Para todos los clientes, con dinero de verdad.</summary>
        Activo
    }

    /// <summary>
    /// NestoAPI#864: el interruptor PROPIO de este canal (independiente del de la app de clientes, #663), el proveedor activo
    /// y los plazos de aviso, en ParametrosUsuario de «(defecto)» (empresa 1): se cambian sin publicar. Los límites de importe
    /// y el plazo de captura no están aquí: son del proveedor (<see cref="CondicionesProveedorPagoAPlazos"/>).
    /// </summary>
    public class ConfiguracionPagoAPlazos
    {
        public const string USUARIO_DEFECTO = "(defecto)";
        /// <summary>«Apagado» (o sin fila), «Pruebas» o «Activo».</summary>
        public const string CLAVE_INTERRUPTOR = "PagoAPlazosPedidos";
        /// <summary>Clientes (separados por comas) a los que se les ofrece en modo Pruebas.</summary>
        public const string CLAVE_CLIENTES_PRUEBA = "PagoAPlazosPedidosClientesPrueba";
        /// <summary>Código del proveedor activo en <see cref="RegistroProveedoresPagoAPlazos"/> («Aplazame»).</summary>
        public const string CLAVE_PROVEEDOR = "PagoAPlazosProveedor";
        /// <summary>Con cuántos días de margen se avisa de que un pedido aprobado sigue sin salir.</summary>
        public const string CLAVE_DIAS_AVISO = "PagoAPlazosDiasAviso";
        /// <summary>Días que vale una solicitud que el cliente no termina; después caduca.</summary>
        public const string CLAVE_DIAS_VALIDEZ = "PagoAPlazosDiasValidezSolicitud";

        public const int DIAS_AVISO_POR_DEFECTO = 3;
        public const int DIAS_VALIDEZ_POR_DEFECTO = 7;

        public ModoPagoAPlazos Modo { get; set; }
        public ISet<string> ClientesPrueba { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string Proveedor { get; set; }
        public int DiasAviso { get; set; } = DIAS_AVISO_POR_DEFECTO;
        public int DiasValidezSolicitud { get; set; } = DIAS_VALIDEZ_POR_DEFECTO;

        public bool Pruebas => Modo == ModoPagoAPlazos.Pruebas;

        public static ConfiguracionPagoAPlazos Leer(ILectorParametrosUsuario lector)
        {
            string Parametro(string clave)
            {
                try
                {
                    return lector?.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, clave);
                }
                catch (Exception)
                {
                    // Sin poder leer el interruptor, apagado: un fallo de lectura no puede encender el pago
                    return null;
                }
            }

            return new ConfiguracionPagoAPlazos
            {
                Modo = InterpretarModo(Parametro(CLAVE_INTERRUPTOR)),
                ClientesPrueba = InterpretarClientes(Parametro(CLAVE_CLIENTES_PRUEBA)),
                Proveedor = Parametro(CLAVE_PROVEEDOR)?.Trim(),
                DiasAviso = InterpretarDias(Parametro(CLAVE_DIAS_AVISO), DIAS_AVISO_POR_DEFECTO),
                DiasValidezSolicitud = InterpretarDias(Parametro(CLAVE_DIAS_VALIDEZ), DIAS_VALIDEZ_POR_DEFECTO)
            };
        }

        internal static ModoPagoAPlazos InterpretarModo(string valor)
        {
            switch (valor?.Trim().ToLowerInvariant())
            {
                case "pruebas":
                case "sandbox":
                    return ModoPagoAPlazos.Pruebas;
                case "activo":
                    return ModoPagoAPlazos.Activo;
                default:
                    return ModoPagoAPlazos.Apagado;
            }
        }

        internal static ISet<string> InterpretarClientes(string valor)
        {
            return new HashSet<string>(
                (valor ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Un número de días entre 1 y 60; lo que no lo sea, el valor por defecto.</summary>
        internal static int InterpretarDias(string valor, int porDefecto)
        {
            return int.TryParse(valor?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dias) && dias >= 1 && dias <= 60
                ? dias
                : porDefecto;
        }

        /// <summary>¿Se le ofrece a este cliente? Apagado: a nadie; Pruebas: solo a los de la lista; Activo: a todos.</summary>
        public bool SeOfreceA(string cliente)
        {
            switch (Modo)
            {
                case ModoPagoAPlazos.Activo:
                    return true;
                case ModoPagoAPlazos.Pruebas:
                    return ClientesPrueba.Contains(cliente?.Trim() ?? string.Empty);
                default:
                    return false;
            }
        }
    }
}
