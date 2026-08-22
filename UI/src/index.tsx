import { ModRegistrar } from "cs2/modding";
import { SellTiles } from "./sell-tiles";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopRight", SellTiles);
};

export default register;
