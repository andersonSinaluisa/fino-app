import { useState } from 'react';
import { Alert, Pressable, RefreshControl, ScrollView, StyleSheet, Switch, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, spacing } from '../../theme';
import { Badge, Card, EmptyState, SkeletonCard, Typo } from '../../components/ui';
import {
  useCategories,
  useCategorizationRules,
  useCreateCategory,
  useDeleteCategorizationRule,
  useUpdateCategorizationRule,
  useUpdateCategory,
} from '../../hooks/queries';
import { ApiError } from '../../services/apiClient';
import { CategoryChipList } from '../../components/categories/CategoryChipList';
import { CategoryFormSheet } from '../../components/categories/CategoryFormSheet';
import { formatRelativeTime } from '../../utils/format';
import type { CategorizationRule, Category } from '../../types/api';

const MATCH_TYPE_LABEL: Record<CategorizationRule['matchType'], string> = {
  Contains: 'contiene',
  StartsWith: 'empieza con',
  Exact: 'es exactamente',
};

/**
 * "Categorización personal" (punto 15): "Reglas de categorización", accesible
 * desde Ajustes. Muestra lo que Fino aprendió de las correcciones manuales de
 * la persona -- nunca las de otro usuario (el backend ya aísla esto por
 * UserId) ni las reglas propias de Fino, que no tienen dueño y no se editan
 * aquí. Editar/activar/desactivar/borrar, todo sin tocar el historial salvo
 * que la persona lo pida explícitamente (puntos 16/17).
 */
export default function CategorizationRulesScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();

  const { data: rules, isLoading, refetch, isRefetching } = useCategorizationRules();
  const { data: categories } = useCategories();
  const updateRule = useUpdateCategorizationRule();
  const deleteRule = useDeleteCategorizationRule();

  const [editingRuleId, setEditingRuleId] = useState<string | null>(null);

  // Categorías personalizadas: crear/editar una categoría propia sin salir
  // de "Cambiar categoría" de una regla. `categoryFormForRule` recuerda para
  // qué regla era el "+ Nueva" que abrió la hoja (null si se abrió para editar).
  const [categoryFormVisible, setCategoryFormVisible] = useState(false);
  const [categoryFormTarget, setCategoryFormTarget] = useState<Category | null>(null);
  const [categoryFormForRule, setCategoryFormForRule] = useState<CategorizationRule | null>(null);
  const [categoryFormError, setCategoryFormError] = useState<string | null>(null);
  const createCategory = useCreateCategory();
  const updateCategory = useUpdateCategory();

  const confirmDelete = (rule: CategorizationRule) => {
    Alert.alert(
      'Borrar regla',
      `"${rule.pattern}" dejará de categorizar movimientos nuevos automáticamente. Los movimientos que ya tienen categoría por esta regla no cambian.`,
      [
        { text: 'Cancelar', style: 'cancel' },
        { text: 'Borrar', style: 'destructive', onPress: () => deleteRule.mutate(rule.id) },
      ],
    );
  };

  const changeCategory = (rule: CategorizationRule, category: Category) => {
    if (category.id === rule.categoryId) {
      setEditingRuleId(null);
      return;
    }

    // Punto 16: cambiar la categoría de una regla nunca toca el historial en
    // automático -- siempre se pregunta primero.
    Alert.alert(
      `Cambiar "${rule.pattern}" a ${category.name}`,
      'Los movimientos que ya categorizó esta regla no cambian solos, ¿quieres actualizarlos también?',
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Solo futuros movimientos',
          onPress: () => {
            updateRule.mutate({ id: rule.id, categoryId: category.id, isActive: rule.isActive });
            setEditingRuleId(null);
          },
        },
        {
          text: 'También los anteriores',
          onPress: () => {
            updateRule.mutate({
              id: rule.id,
              categoryId: category.id,
              isActive: rule.isActive,
              applyToExistingMatches: true,
            });
            setEditingRuleId(null);
          },
        },
      ],
    );
  };

  const toggleActive = (rule: CategorizationRule) => {
    updateRule.mutate({ id: rule.id, categoryId: rule.categoryId, isActive: !rule.isActive });
  };

  const openCreateCategory = (rule: CategorizationRule) => {
    setCategoryFormError(null);
    setCategoryFormTarget(null);
    setCategoryFormForRule(rule);
    setCategoryFormVisible(true);
  };

  const openEditCategory = (category: Category) => {
    setCategoryFormError(null);
    setCategoryFormTarget(category);
    setCategoryFormForRule(null);
    setCategoryFormVisible(true);
  };

  const submitCategoryForm = (input: { name: string; icon: string; color: string }) => {
    setCategoryFormError(null);

    if (categoryFormTarget) {
      updateCategory.mutate(
        { id: categoryFormTarget.id, ...input },
        {
          onSuccess: () => setCategoryFormVisible(false),
          onError: (error) => {
            setCategoryFormError(error instanceof ApiError ? error.message : 'No se pudo guardar la categoría.');
          },
        },
      );
      return;
    }

    createCategory.mutate(input, {
      onSuccess: (created) => {
        setCategoryFormVisible(false);
        // Reutiliza el mismo flujo que elegir una categoría existente --
        // sigue preguntando el alcance (solo futuros / también anteriores).
        if (categoryFormForRule) {
          changeCategory(categoryFormForRule, created);
        }
      },
      onError: (error) => {
        setCategoryFormError(error instanceof ApiError ? error.message : 'No se pudo crear la categoría.');
      },
    });
  };

  return (
    <>
    <View style={[styles.root, { paddingTop: insets.top + spacing.sm }]}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="close" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Cerrar
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">Reglas de categorización</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Lo que Fino aprendió de tus correcciones. Son solo tuyas -- nunca afectan a otras cuentas.
        </Typo>
      </View>

      <ScrollView
        contentContainerStyle={[styles.list, { paddingBottom: insets.bottom + 120 }]}
        showsVerticalScrollIndicator={false}
        refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} tintColor={colors.textSecondary} />}
      >
          {isLoading ? (
            <>
              <SkeletonCard />
              <SkeletonCard />
            </>
          ) : !rules || rules.length === 0 ? (
            <EmptyState
              icon="pricetags-outline"
              title="Todavía no tienes reglas"
              body='Cuando cambies la categoría de un movimiento y elijas "aplicar también a movimientos similares", aparecerá aquí.'
            />
          ) : (
            rules.map((rule) => (
              <Card key={rule.id} style={styles.ruleCard}>
                <View style={styles.ruleTop}>
                  <View style={styles.ruleTitleBlock}>
                    <Typo variant="body">
                      Si la descripción {MATCH_TYPE_LABEL[rule.matchType]} "{rule.pattern}"
                    </Typo>
                    <View style={styles.ruleCategoryRow}>
                      <View style={[styles.categoryDot, { backgroundColor: rule.categoryColor }]} />
                      <Typo variant="bodyStrong">{rule.categoryName}</Typo>
                      {!rule.isActive ? <Badge label="Desactivada" tone="neutral" /> : null}
                    </View>
                  </View>
                  <Switch value={rule.isActive} onValueChange={() => toggleActive(rule)} />
                </View>

                <Typo variant="caption" color={colors.textSecondary} style={styles.ruleHint}>
                  {rule.matchCount > 0
                    ? `Aplicada ${rule.matchCount} ${rule.matchCount === 1 ? 'vez' : 'veces'}${
                        rule.lastMatchedAt ? ` · última vez ${formatRelativeTime(rule.lastMatchedAt)}` : ''
                      }`
                    : 'Todavía no se aplicó a ningún movimiento.'}
                </Typo>

                <View style={styles.ruleActions}>
                  <Pressable
                    onPress={() => setEditingRuleId(editingRuleId === rule.id ? null : rule.id)}
                    style={styles.ruleActionButton}
                  >
                    <Ionicons name="pencil-outline" size={15} color={colors.textSecondary} />
                    <Typo variant="caption" color={colors.textSecondary}>
                      {editingRuleId === rule.id ? 'Cancelar' : 'Cambiar categoría'}
                    </Typo>
                  </Pressable>
                  <Pressable onPress={() => confirmDelete(rule)} style={styles.ruleActionButton}>
                    <Ionicons name="trash-outline" size={15} color={colors.danger} />
                    <Typo variant="caption" color={colors.danger}>
                      Borrar
                    </Typo>
                  </Pressable>
                </View>

                {editingRuleId === rule.id ? (
                  <CategoryChipList
                    categories={categories ?? []}
                    selectedId={rule.categoryId}
                    onSelect={(category) => changeCategory(rule, category)}
                    onCreateNew={() => openCreateCategory(rule)}
                    onEdit={openEditCategory}
                  />
                ) : null}
              </Card>
            ))
          )}
      </ScrollView>
    </View>

    <CategoryFormSheet
      visible={categoryFormVisible}
      category={categoryFormTarget}
      onClose={() => setCategoryFormVisible(false)}
      onSubmit={submitCategoryForm}
      submitting={createCategory.isPending || updateCategory.isPending}
      errorMessage={categoryFormError}
    />
    </>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginHorizontal: spacing.xl,
    marginBottom: spacing.lg,
  },
  header: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.lg,
    gap: 2,
  },
  list: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  ruleCard: {
    gap: spacing.sm,
  },
  ruleTop: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  ruleTitleBlock: {
    flex: 1,
    gap: spacing.xs,
  },
  ruleCategoryRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  categoryDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
  },
  ruleHint: {
    marginTop: -spacing.xs,
  },
  ruleActions: {
    flexDirection: 'row',
    gap: spacing.lg,
    marginTop: spacing.xs,
  },
  ruleActionButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
});
