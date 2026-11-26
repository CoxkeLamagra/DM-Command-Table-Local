import { getDatabase } from "@/db/sqlite";
import { getUser } from "@/server/identity/auth";
import { requireCampaignEdit } from "@/server/campaigns/access";
import { apiError, apiJson, jsonBody } from "@/server/http/http";
import { rejectCrossOrigin } from "@/server/http/origin";
import { enforceRateLimit } from "@/server/http/request-security";
import { parseInput } from "@/server/http/validation";
import { idSchema } from "@/server/http/schemas";
import { convertCatalogueMonster } from "@/features/bestiary/fiveetools";
import {
  parseCatalogue,
  searchCatalogue,
} from "@/features/bestiary/local-catalogue";
import { catalogueTokenUrl } from "@/features/bestiary/monster-token";
import {
  loadRemoteCatalogue,
  searchRemoteCatalogue,
} from "@/features/bestiary/remote-catalogue";
import { z } from "zod";

export const runtime = "nodejs";
const inputSchema = z.object({
  campaignId: idSchema,
  query: z.string().trim().min(2).max(200),
  local: z.string().optional(),
});
let remoteIndex: Awaited<ReturnType<typeof loadRemoteCatalogue>> | undefined;
const remoteFiles = new Map();

export async function POST(request: Request) {
  if (process.env.DM_COMMAND_TABLE_DESKTOP !== "1")
    return apiJson({ error: "Desktop catalogue unavailable." }, 404);
  const originError = rejectCrossOrigin(request);
  if (originError) return originError;
  try {
    const database = getDatabase();
    const user = await getUser(database);
    if (!user) return apiJson({ error: "Authentication required." }, 401);
    const limited = enforceRateLimit(
      database,
      request,
      "desktop-catalogue",
      user.userId,
      60,
      60_000,
    );
    if (limited) return limited;
    // The wrapper can expand a 10 MiB source through JSON escaping.
    const { campaignId, query, local } = parseInput(
      inputSchema,
      await jsonBody(request, 22 * 1024 * 1024),
    );
    requireCampaignEdit(database, campaignId, user.userId);
    if (local !== undefined && Buffer.byteLength(local) > 10 * 1024 * 1024)
      return apiJson(
        { error: "Catalogue files must be no larger than 10 MiB." },
        413,
      );
    let matches;
    if (local !== undefined) {
      matches = searchCatalogue(query, parseCatalogue(local));
    } else {
      try {
        remoteIndex ??= await loadRemoteCatalogue(request.signal);
        matches = await searchRemoteCatalogue(
          query,
          remoteIndex,
          remoteFiles,
          request.signal,
        );
      } catch {
        return apiJson(
          {
            error:
              "Online Bestiary unavailable. Try again or use a local catalogue file.",
          },
          502,
        );
      }
    }
    return apiJson({
      monsters: matches.map((entry) => {
        const monster = convertCatalogueMonster(entry, () =>
          crypto.randomUUID(),
        );
        return {
          tokenUrl: catalogueTokenUrl(entry),
          monster: {
            name: monster.name,
            type: monster.type,
            challengeRating: monster.cr,
            armorClass: monster.ac,
            hitPoints: monster.hp,
            speed: monster.speed,
            stats: monster.stats,
            token: "",
            abilities: monster.abilities,
            spells: monster.spells,
            notes: "",
            spellSlots: monster.slots,
            source: monster.source ?? null,
            favorite: false,
            tagIds: [],
            catalogueImport: true,
          },
        };
      }),
    });
  } catch (error) {
    return apiError(error);
  }
}
